using System.ComponentModel;
using System.Globalization;
using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Wordsbook.Controls;
using Wordsbook.Models;
using Wordsbook.Services;
using Wordsbook.ViewModels;

namespace Wordsbook;

/// <summary>
/// 常驻置顶的单词卡小窗：桌面 acrylic、无系统标题栏、高度随内容向下生长。
/// 贴边收起后只剩一条窄边，点一下展开。
/// </summary>
public sealed partial class ScreenCardWindow : Window
{
    const double CardWidth = 380;
    const double HeaderHeight = 40;
    const double CollapsedMaxWidth = 112;
    const double CollapsedMaxHeight = 184;
    const double CollapsedPadding = 8;
    const double CollapsedTabWidth = 56;
    const double CollapsedTabHeight = 96;

    static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    readonly AppViewModel _vm;
    readonly IntPtr _hwnd;
    readonly DispatcherQueueTimer _timer;
    readonly SolidColorBrush _glyphHidden = new(Windows.UI.Color.FromArgb(0x00, 0x00, 0x00, 0x00));
    readonly SolidColorBrush _glyphShown = new(Windows.UI.Color.FromArgb(0x88, 0x00, 0x00, 0x00));
    readonly SolidColorBrush _transparent = new(Windows.UI.Color.FromArgb(0x00, 0x00, 0x00, 0x00));

    OverlappedPresenter? _presenter;
    SystemBackdrop? _windowBackdrop;

    List<WordEntry> _queue = [];
    int _queueIndex;
    WordEntry? _current;
    int _imageLoadToken;
    string? _collapsedImagePath;
    bool _hasCollapsedImage;
    double _collapsedImageWidth;
    double _collapsedImageHeight;
    List<(int X0, int X1, int Y0, int Y1)> _alphaRects = [];
    int _alphaWidth;
    int _alphaHeight;

    Flyout? _settingsFlyout;
    bool _flyoutOpen;
    readonly List<(string? Id, ToggleButton Chip)> _langChips = [];
    readonly Dictionary<string, ToggleButton> _dateChips = [];
    readonly Dictionary<string, ToggleButton> _catChips = [];
    TextBlock? _poolLabel;

    bool _collapsed;
    bool _collapseToLeft;
    bool _dragging;
    (int X, int Y) _dragCursor;
    Windows.Graphics.PointInt32 _dragWindow;
    Windows.Graphics.PointInt32 _anchor;
    double _lastHeightDip = 360;
    double _collapsedContentWidth;
    double _collapsedContentHeight;

    public ScreenCardWindow(AppViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

        Title = "Wordsbook card";
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        _windowBackdrop = SystemBackdrop;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // 留系统边框但不要标题栏：DWM 才会按 Win11 规则给窗口切圆角。
            _presenter = presenter;
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        WindowChrome.UseNativeCorners(_hwnd);

        _timer = DispatcherQueue.CreateTimer();
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => ShowNextCard();

        Root.RequestedTheme = _vm.Theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        CloseButton.Content = Icons.Cross;
        CloseButton.Foreground = _glyphHidden;
        SettingsButton.Content = Icons.Settings;
        CollapseButton.Content = Icons.Dock;
        NextButton.Content = Icons.ChevronRight;

        RestorePlacement();
        UpdatePauseButton();
        UpdateStatus();
        RestartTimer();
        ShowNextCard();

        _vm.PropertyChanged += OnVmChanged;
        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;
    }

    // ------------------------------------------------------------ 位置 / 尺寸

    Windows.Graphics.RectInt32 WorkArea() =>
        DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;

    static int Right(Windows.Graphics.RectInt32 rect) => rect.X + rect.Width;

    static int Bottom(Windows.Graphics.RectInt32 rect) => rect.Y + rect.Height;

    /// <summary>
    /// 窗口尺寸按客户区内容反推：无边框窗口仍有几像素非客户区，直接拿内容高度当窗口高度
    /// 会把卡片底部（日期、状态角标）裁掉。
    /// </summary>
    (int Width, int Height) WindowSizeFor(double dipWidth, double dipHeight, double dpi)
    {
        var client = WindowChrome.ClientSize(_hwnd);
        var size = AppWindow.Size;
        var frameX = client.Width > 0 ? Math.Max(0, size.Width - client.Width) : 0;
        var frameY = client.Height > 0 ? Math.Max(0, size.Height - client.Height) : 0;
        return (
            (int)Math.Round(dipWidth * dpi) + frameX,
            (int)Math.Round(dipHeight * dpi) + frameY);
    }

    void RestorePlacement()
    {
        var dpi = WindowChrome.DpiScale(_hwnd);
        var area = WorkArea();
        var (w, h) = WindowSizeFor(CardWidth, _lastHeightDip, dpi);

        var settings = _vm.ScreenCard;
        int x, y;
        if (settings.WindowX is int storedX && settings.WindowY is int storedY)
        {
            x = Math.Clamp(storedX, area.X, Math.Max(area.X, Right(area) - w));
            y = Math.Clamp(storedY, area.Y, Math.Max(area.Y, Bottom(area) - h));
        }
        else
        {
            x = Math.Max(area.X, Right(area) - w - (int)Math.Round(28 * dpi));
            y = area.Y + (int)Math.Round(56 * dpi);
        }

        _anchor = new Windows.Graphics.PointInt32(x, y);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));
        UpdateCollapseGlyph();
    }

    /// <summary>
    /// 高度按内容自适应，锚点留在用户放下的顶边，所以窗口只往下长。
    /// 长到工作区底边时改为贴着底边停住。
    /// </summary>
    void SyncWindowSize()
    {
        // 改筛选会换卡片、改高度；窗口一缩一放 Flyout 就会被系统收掉，等它关了再量。
        if (_collapsed || _flyoutOpen) return;
        if (_current is not null && CardBody.ActualHeight < 1) return;

        var dpi = WindowChrome.DpiScale(_hwnd);
        var area = WorkArea();
        var dip = _current is null
            ? 150
            : HeaderHeight + CardBody.ActualHeight + 14 + 2;
        var maxDip = Math.Max(160, area.Height / dpi - 24);
        dip = Math.Min(dip, maxDip);
        _lastHeightDip = dip;

        var (w, h) = WindowSizeFor(CardWidth, dip, dpi);
        var size = AppWindow.Size;
        if (Math.Abs(size.Width - w) <= 1 && Math.Abs(size.Height - h) <= 1) return;

        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));

        var x = Math.Clamp(_anchor.X, area.X, Math.Max(area.X, Right(area) - w));
        var y = _anchor.Y + h <= Bottom(area) ? _anchor.Y : Math.Max(area.Y, Bottom(area) - h);
        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    void OnCardBodySizeChanged(object sender, SizeChangedEventArgs e) => SyncWindowSize();

    void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange && _collapsed)
        {
            ApplyCollapsedRegion(WindowChrome.DpiScale(_hwnd));
        }
        if (!_collapsed && args.DidPositionChange) UpdateCollapseGlyph();
    }

    // ------------------------------------------------------------ 拖动

    void OnHeaderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Header).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        _dragCursor = WindowChrome.CursorPosition();
        _dragWindow = AppWindow.Position;
        Header.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    void OnHeaderPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        if (!e.GetCurrentPoint(Header).Properties.IsLeftButtonPressed)
        {
            EndDrag(e);
            return;
        }

        var cursor = WindowChrome.CursorPosition();
        AppWindow.Move(new Windows.Graphics.PointInt32(
            _dragWindow.X + cursor.X - _dragCursor.X,
            _dragWindow.Y + cursor.Y - _dragCursor.Y));
    }

    void OnHeaderPointerReleased(object sender, PointerRoutedEventArgs e) => EndDrag(e);

    void EndDrag(PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Header.ReleasePointerCapture(e.Pointer);

        // 拖出屏幕后存下来的位置下次会捞不回来，松手时夹回工作区。
        var area = WorkArea();
        var size = AppWindow.Size;
        var position = AppWindow.Position;
        var x = Math.Clamp(position.X, area.X, Math.Max(area.X, Right(area) - size.Width));
        var y = Math.Clamp(position.Y, area.Y, Math.Max(area.Y, Bottom(area) - size.Height));
        if (x != position.X || y != position.Y) AppWindow.Move(new Windows.Graphics.PointInt32(x, y));

        _anchor = AppWindow.Position;
        UpdateCollapseGlyph();
        SavePosition();
    }

    void SavePosition()
    {
        var position = AppWindow.Position;
        _vm.SaveScreenCard(s =>
        {
            s.WindowX = position.X;
            s.WindowY = position.Y;
        });
    }

    // ------------------------------------------------------------ 贴边收起 / 展开

    void UpdateCollapseGlyph()
    {
        var area = WorkArea();
        var position = AppWindow.Position;
        _collapseToLeft = position.X + AppWindow.Size.Width / 2 <= area.X + area.Width / 2;
        ToolTipService.SetToolTip(CollapseButton,
            _collapseToLeft ? "Tuck to the left edge" : "Tuck to the right edge");
    }

    void OnCollapseClick(object sender, RoutedEventArgs e)
    {
        if (_collapsed) return;

        _anchor = AppWindow.Position;
        SavePosition();
        UpdateCollapseGlyph();

        _collapsed = true;
        ExpandedRoot.Visibility = Visibility.Collapsed;
        CollapsedRoot.Visibility = Visibility.Visible;
        // 收起后不要底色，只留图片本身。
        Shell.Background = _transparent;
        EnterCollapsedChrome();

        UpdateCollapsedVisual();
        LayoutCollapsedTab();
    }

    /// <summary>
    /// 收起态彻底去掉窗口边框和所有背景层：系统边框会留下白边/白条，
    /// acrylic 和 Mica 则会在图片周围铺一层底色。直角交给 DWM 关掉圆角，
    /// 否则小窗的圆角会把图片边缘切掉。
    /// </summary>
    void EnterCollapsedChrome()
    {
        _presenter?.SetBorderAndTitleBar(false, false);
        WindowChrome.UseNativeCorners(_hwnd, round: false);
        WindowChrome.HideWindowBorder(_hwnd, true);
        SystemBackdrop = null;
        CardGlass.Visibility = Visibility.Collapsed;
    }

    void EnterExpandedChrome()
    {
        _presenter?.SetBorderAndTitleBar(true, false);
        WindowChrome.UseNativeCorners(_hwnd);
        WindowChrome.HideWindowBorder(_hwnd, false);
        SystemBackdrop = _windowBackdrop;
        CardGlass.Visibility = Visibility.Visible;
    }

    /// <summary>收起态的窗口缩到图片大小；系统最小宽度多出来的透明部分用矩形区域裁掉。</summary>
    void LayoutCollapsedTab()
    {
        if (!_collapsed) return;

        var dpi = WindowChrome.DpiScale(_hwnd);
        var area = WorkArea();

        _collapsedContentWidth = _hasCollapsedImage
            ? _collapsedImageWidth + CollapsedPadding * 2
            : CollapsedTabWidth + CollapsedPadding * 2;
        _collapsedContentHeight = _hasCollapsedImage
            ? _collapsedImageHeight + CollapsedPadding * 2
            : CollapsedTabHeight + CollapsedPadding * 2;

        // 箭头指向屏幕内侧：贴左边朝右，贴右边朝左。
        CollapseChevron.Text = _collapseToLeft ? Icons.ChevronRight : Icons.ChevronLeft;

        CollapsedRoot.Width = _collapsedContentWidth;
        CollapsedRoot.Height = _collapsedContentHeight;
        CollapsedRoot.HorizontalAlignment = _collapseToLeft
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        CollapsedRoot.VerticalAlignment = VerticalAlignment.Top;

        var edge = _collapseToLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        var margin = _collapseToLeft
            ? new Thickness(CollapsedPadding, 0, 0, 0)
            : new Thickness(0, 0, CollapsedPadding, 0);
        CollapsedImage.HorizontalAlignment = edge;
        CollapsedImage.Margin = margin;
        CollapsedFallback.HorizontalAlignment = edge;
        CollapsedFallback.Margin = margin;

        var (w, h) = WindowSizeFor(_collapsedContentWidth, _collapsedContentHeight, dpi);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));

        var actual = AppWindow.Size;
        var x = _collapseToLeft ? area.X : Math.Max(area.X, Right(area) - actual.Width);
        var y = Math.Clamp(_anchor.Y, area.Y, Math.Max(area.Y, Bottom(area) - actual.Height));
        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));

        // 收起后元素刚变为可见，ActualWidth/布局位置要等这一轮布局跑完才准。
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,
            () => ApplyCollapsedRegion(WindowChrome.DpiScale(_hwnd)));
    }

    /// <summary>
    /// 有图片时按图片 alpha 抠出窗口区域，透明背景直接透出桌面；
    /// 没有图片时留一个圆角标签（用不透明底色填满，不会有黑底）。
    /// 区域坐标必须落在客户区里，窗口边框那几像素不参与。
    /// </summary>
    void ApplyCollapsedRegion(double dpi)
    {
        if (!_collapsed) return;

        var client = WindowChrome.ClientOrigin(_hwnd);
        if (WindowChrome.ClientSize(_hwnd).Width < 1) return;

        // 区域必须和 XAML 实际排布对齐：宽度受父容器、边距和 DPI 共同影响，
        // 按“理论尺寸”算会差出一截，露出窗口黑底。
        if (_hasCollapsedImage && _collapsedImageWidth >= 1 && _collapsedImageHeight >= 1)
        {
            if (TryRegionOf(CollapsedImage, client, dpi, out var imageRect))
            {
                if (_alphaRects.Count > 0 && _alphaWidth > 0 && _alphaHeight > 0)
                {
                    var scaleX = imageRect.Width / (double)_alphaWidth;
                    var scaleY = imageRect.Height / (double)_alphaHeight;
                    var rects = new List<(int X, int Y, int Width, int Height)>(_alphaRects.Count);
                    foreach (var run in _alphaRects)
                    {
                        var x0 = imageRect.X + (int)Math.Round(run.X0 * scaleX);
                        var x1 = imageRect.X + (int)Math.Round(run.X1 * scaleX);
                        var y0 = imageRect.Y + (int)Math.Round(run.Y0 * scaleY);
                        var y1 = imageRect.Y + (int)Math.Round(run.Y1 * scaleY);
                        if (x1 > x0 && y1 > y0) rects.Add((x0, y0, x1 - x0, y1 - y0));
                    }

                    if (rects.Count > 0)
                    {
                        WindowChrome.ApplyRegionRects(_hwnd, rects);
                        return;
                    }
                }

                // 解不出 alpha 时至少保证图片完整，不缩成兜底标签。
                WindowChrome.ApplyRectRegion(_hwnd, imageRect.X, imageRect.Y, imageRect.Width, imageRect.Height);
                return;
            }
        }

        if (TryRegionOf(CollapsedFallback, client, dpi, out var tabRect))
            WindowChrome.ApplyRoundRectRegion(_hwnd, tabRect.X, tabRect.Y,
                tabRect.Width, tabRect.Height, 16, dpi);
    }

    /// <summary>元素在客户区里的物理像素矩形（含窗口边框偏移）。</summary>
    bool TryRegionOf(FrameworkElement element, (int X, int Y) client, double dpi,
        out (int X, int Y, int Width, int Height) rect)
    {
        rect = default;
        if (element.ActualWidth < 1 || element.ActualHeight < 1) return false;

        var origin = element.TransformToVisual(Root)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        rect = (
            client.X + (int)Math.Round(origin.X * dpi),
            client.Y + (int)Math.Round(origin.Y * dpi),
            Math.Max(1, (int)Math.Round(element.ActualWidth * dpi)),
            Math.Max(1, (int)Math.Round(element.ActualHeight * dpi)));
        return true;
    }

    void OnExpandTapped(object sender, TappedRoutedEventArgs e)
    {
        if (!_collapsed) return;

        _collapsed = false;
        CollapsedRoot.Visibility = Visibility.Collapsed;
        CollapsedRoot.ClearValue(FrameworkElement.WidthProperty);
        CollapsedRoot.ClearValue(FrameworkElement.HeightProperty);
        CollapsedRoot.HorizontalAlignment = HorizontalAlignment.Stretch;
        ExpandedRoot.Visibility = Visibility.Visible;
        Shell.ClearValue(Border.BackgroundProperty);
        WindowChrome.ClearRegion(_hwnd);
        EnterExpandedChrome();

        var dpi = WindowChrome.DpiScale(_hwnd);
        var (w, h) = WindowSizeFor(CardWidth, Math.Max(180, _lastHeightDip), dpi);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
        AppWindow.Move(_anchor);
        DispatcherQueue.TryEnqueue(SyncWindowSize);
    }

    void UpdateCollapsedVisual()
    {
        var path = _vm.ScreenCardImagePath;
        if (path == _collapsedImagePath)
        {
            if (!_hasCollapsedImage && _collapsed) LayoutCollapsedTab();
            return;
        }

        _collapsedImagePath = path;
        _hasCollapsedImage = false;
        _alphaRects = [];
        _alphaWidth = 0;
        _alphaHeight = 0;
        CollapsedImage.Source = null;
        CollapsedImage.Visibility = Visibility.Collapsed;
        CollapsedFallback.Visibility = Visibility.Visible;

        if (path.Length == 0 || !File.Exists(path))
        {
            if (_collapsed) LayoutCollapsedTab();
            return;
        }

        _ = LoadCollapsedImageAsync(path, ++_imageLoadToken);
    }

    /// <summary>
    /// 未打包应用里 BitmapImage 直接吃 file:/// 不可靠，改成先拿 StorageFile 再喂流。
    /// 图片按最大尺寸等比缩放后完整显示，不做裁剪。
    /// </summary>
    async Task LoadCollapsedImageAsync(string path, int token)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (token != _imageLoadToken || path != _collapsedImagePath) return;

            var scale = Math.Min(1d, Math.Min(
                CollapsedMaxWidth / Math.Max(1, bitmap.PixelWidth),
                CollapsedMaxHeight / Math.Max(1, bitmap.PixelHeight)));
            _collapsedImageWidth = Math.Max(1, bitmap.PixelWidth * scale);
            _collapsedImageHeight = Math.Max(1, bitmap.PixelHeight * scale);
            _hasCollapsedImage = true;

            var (mask, maskW, maskH) = await BuildAlphaMaskAsync(file,
                (int)Math.Round(_collapsedImageWidth),
                (int)Math.Round(_collapsedImageHeight));
            if (token != _imageLoadToken || path != _collapsedImagePath) return;
            _alphaRects = mask;
            _alphaWidth = maskW;
            _alphaHeight = maskH;

            CollapsedImage.Width = _collapsedImageWidth;
            CollapsedImage.Height = _collapsedImageHeight;
            CollapsedImage.Source = bitmap;
            CollapsedImage.Visibility = Visibility.Visible;
            CollapsedFallback.Visibility = Visibility.Collapsed;
            if (_collapsed) LayoutCollapsedTab();
        }
        catch
        {
            if (token != _imageLoadToken) return;
            _hasCollapsedImage = false;
            _alphaRects = [];
            _alphaWidth = 0;
            _alphaHeight = 0;
            if (_collapsed) LayoutCollapsedTab();
        }
    }

    /// <summary>
    /// 把图片 alpha 通道采样成显示分辨率下的不透明矩形（按行合并同宽度的连续段），
    /// 交给 SetWindowRgn 后窗口只保留人物轮廓，透明背景透出桌面。
    /// </summary>
    static async Task<(List<(int X0, int X1, int Y0, int Y1)> Rects, int Width, int Height)>
        BuildAlphaMaskAsync(Windows.Storage.StorageFile file, int displayWidth, int displayHeight)
    {
        displayWidth = Math.Max(1, displayWidth);
        displayHeight = Math.Max(1, displayHeight);

        using var stream = await file.OpenReadAsync();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        using var software = await decoder.GetSoftwareBitmapAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Straight);

        var sourceWidth = software.PixelWidth;
        var sourceHeight = software.PixelHeight;
        if (sourceWidth < 1 || sourceHeight < 1) return ([], displayWidth, displayHeight);

        var bytes = new byte[sourceWidth * sourceHeight * 4];
        var buffer = new Windows.Storage.Streams.Buffer((uint)bytes.Length);
        software.CopyToBuffer(buffer);
        Windows.Storage.Streams.DataReader.FromBuffer(buffer).ReadBytes(bytes);

        const byte threshold = 128;
        var rects = new List<(int X0, int X1, int Y0, int Y1)>();
        List<(int X0, int X1)>? group = null;
        var groupStart = 0;

        void Flush(List<(int X0, int X1)> runs, int y0, int y1)
        {
            foreach (var (x0, x1) in runs) rects.Add((x0, x1, y0, y1));
        }

        for (var y = 0; y < displayHeight; y++)
        {
            var sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / displayHeight);
            var runs = new List<(int X0, int X1)>();
            var runStart = -1;
            var row = sourceY * sourceWidth;
            for (var x = 0; x < displayWidth; x++)
            {
                var sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / displayWidth);
                var opaque = bytes[(row + sourceX) * 4 + 3] >= threshold;
                if (opaque && runStart < 0) runStart = x;
                else if (!opaque && runStart >= 0)
                {
                    runs.Add((runStart, x));
                    runStart = -1;
                }
            }
            if (runStart >= 0) runs.Add((runStart, displayWidth));

            if (group is not null && !SameRuns(group, runs))
            {
                Flush(group, groupStart, y);
                group = null;
            }
            if (runs.Count > 0 && group is null)
            {
                group = runs;
                groupStart = y;
            }
        }
        if (group is not null) Flush(group, groupStart, displayHeight);

        return (rects, displayWidth, displayHeight);
    }

    static bool SameRuns(List<(int X0, int X1)> a, List<(int X0, int X1)> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    void OnCollapsedImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _hasCollapsedImage = false;
        _alphaRects = [];
        _alphaWidth = 0;
        _alphaHeight = 0;
        CollapsedImage.Visibility = Visibility.Collapsed;
        CollapsedFallback.Visibility = Visibility.Visible;
        if (_collapsed) LayoutCollapsedTab();
    }

    // ------------------------------------------------------------ 卡片内容

    void ShowNextCard()
    {
        if (_queue.Count == 0 || _queueIndex >= _queue.Count)
        {
            _queue = BuildPool();
            _queueIndex = 0;
        }

        _current = _queue.Count == 0 ? null : _queue[_queueIndex++];
        ShowEntry(_current);
        UpdateCollapsedVisual();
        UpdateStatus();
        DispatcherQueue.TryEnqueue(SyncWindowSize);
    }

    void ShowEntry(WordEntry? entry)
    {
        if (entry is null)
        {
            CardScroll.Visibility = Visibility.Collapsed;
            EmptyHint.Visibility = Visibility.Visible;
            return;
        }

        CardScroll.Visibility = Visibility.Visible;
        EmptyHint.Visibility = Visibility.Collapsed;

        var view = WordView.From(entry, focusMode: false, CardFields.All);

        WordText.Text = view.Word;
        SetText(NatureText, view.Nature);
        SetText(PosText, view.Pos);
        SetText(MeaningText, view.Meaning);
        SetText(PronText, view.Pronunciation);

        RelatedList.ItemsSource = view.RelatedForms;
        VariantsPanel.Visibility = view.RelatedForms.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        SetText(DescriptionText, view.Description);
        DescriptionPanel.Visibility = string.IsNullOrWhiteSpace(view.Description)
            ? Visibility.Collapsed
            : Visibility.Visible;

        MarkBadge.Background = view.MarkFill;
        MarkGlyph.Text = view.MarkGlyph;

        var parts = new List<string>();
        if (_vm.Data.Languages.FirstOrDefault(l => l.Id == entry.LangId) is { } language)
            parts.Add(language.Name);
        if (DateTime.TryParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            parts.Add(date.ToString("d MMM yyyy", En));
        var categories = _vm.Data.Categories
            .Where(c => entry.CategoryIds.Contains(c.Id))
            .Select(c => c.Name)
            .ToList();
        if (categories.Count > 0) parts.Add(string.Join(", ", categories));
        MetaText.Text = string.Join("  ·  ", parts);
    }

    static void SetText(TextBlock target, string text)
    {
        target.Text = text;
        target.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>按当前筛选挑出候选词。日期/分类为空表示不限制。</summary>
    IEnumerable<WordEntry> FilteredWords()
    {
        var settings = _vm.ScreenCard;
        IEnumerable<WordEntry> query = _vm.Data.Words;
        if (!string.IsNullOrEmpty(settings.LanguageId))
            query = query.Where(w => w.LangId == settings.LanguageId);
        if (settings.Dates.Count > 0)
        {
            var dates = settings.Dates.ToHashSet();
            query = query.Where(w => dates.Contains(w.Date));
        }
        if (settings.CategoryIds.Count > 0)
        {
            var categories = settings.CategoryIds.ToHashSet();
            query = query.Where(w => w.CategoryIds.Any(categories.Contains));
        }
        return query;
    }

    List<WordEntry> BuildPool()
    {
        var list = FilteredWords().ToList();
        return _vm.ScreenCard.ReviewMode
            ? list.OrderBy(w => w.Mark == 0 ? 1 : 0).ThenBy(_ => Random.Shared.Next()).ToList()
            : list.OrderBy(_ => Random.Shared.Next()).ToList();
    }

    int CountPool() => FilteredWords().Count();

    // ------------------------------------------------------------ 刷新 / 暂停

    void UpdateStatus()
    {
        var settings = _vm.ScreenCard;
        var mode = settings.ReviewMode ? "Review" : "Random";
        StatusText.Text = settings.Paused
            ? $"Paused · every {settings.RefreshMinutes} min"
            : $"{mode} · every {settings.RefreshMinutes} min";
    }

    void UpdatePauseButton()
    {
        var paused = _vm.ScreenCard.Paused;
        PauseButton.Content = paused ? Icons.Play : Icons.Pause;
        ToolTipService.SetToolTip(PauseButton, paused ? "Resume refresh" : "Pause refresh");
    }

    void RestartTimer()
    {
        var settings = _vm.ScreenCard;
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMinutes(Math.Clamp(settings.RefreshMinutes, 1, 60));
        if (!settings.Paused) _timer.Start();
    }

    void OnNextClick(object sender, RoutedEventArgs e)
    {
        ShowNextCard();
        if (!_vm.ScreenCard.Paused) RestartTimer();
    }

    void OnTogglePause(object sender, RoutedEventArgs e)
    {
        _vm.SaveScreenCard(s => s.Paused = !s.Paused);
        UpdatePauseButton();
        UpdateStatus();
        RestartTimer();
    }

    // ------------------------------------------------------------ 设置弹层

    static Style StyleOf(string key) => (Style)Application.Current.Resources[key];

    static TextBlock SectionLabel(string text) =>
        new() { Text = text, Style = StyleOf("SectionHeader") };

    static TextBlock Hint(string text) => new()
    {
        Text = text,
        Style = StyleOf("CardMuted"),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
    };

    static ToggleButton Chip(string text) =>
        new() { Content = text, Style = StyleOf("CategoryChipButton") };

    void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (_settingsFlyout is null)
        {
            _settingsFlyout = new Flyout
            {
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                FlyoutPresenterStyle = StyleOf("DateFlyoutPresenter"),
                // 卡片窗口本身很矮，不放开限制的话弹层会被裁在窗口高度里。
                ShouldConstrainToRootBounds = false,
            };
            _settingsFlyout.Opened += (_, _) => _flyoutOpen = true;
            _settingsFlyout.Closed += (_, _) =>
            {
                _flyoutOpen = false;
                _poolLabel = null;
                DispatcherQueue.TryEnqueue(SyncWindowSize);
            };
        }

        _settingsFlyout.Content = BuildSettingsContent();
        _settingsFlyout.ShowAt(SettingsButton);
    }

    FrameworkElement BuildSettingsContent()
    {
        _langChips.Clear();
        _dateChips.Clear();
        _catChips.Clear();

        var settings = _vm.ScreenCard;
        var root = new StackPanel { Width = 296, Spacing = 14, Margin = new Thickness(2) };

        // ---- 模式
        var modePanel = new StackPanel { Spacing = 6 };
        modePanel.Children.Add(SectionLabel("Mode"));
        var modeWrap = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        var random = Chip("Random");
        var review = Chip("Review");
        random.IsChecked = !settings.ReviewMode;
        review.IsChecked = settings.ReviewMode;
        random.Click += (_, _) =>
        {
            _vm.SaveScreenCard(s => s.ReviewMode = false);
            random.IsChecked = true;
            review.IsChecked = false;
            OnFilterChanged();
        };
        review.Click += (_, _) =>
        {
            _vm.SaveScreenCard(s => s.ReviewMode = true);
            random.IsChecked = false;
            review.IsChecked = true;
            OnFilterChanged();
        };
        modeWrap.Children.Add(random);
        modeWrap.Children.Add(review);
        modePanel.Children.Add(modeWrap);
        modePanel.Children.Add(Hint("Review brings words you have not marked as known to the front."));
        root.Children.Add(modePanel);

        // ---- 语言
        var langPanel = new StackPanel { Spacing = 6 };
        langPanel.Children.Add(SectionLabel("By language"));
        var langWrap = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };

        var allChip = Chip($"All · {_vm.Data.Words.Count}");
        allChip.IsChecked = string.IsNullOrEmpty(settings.LanguageId);
        allChip.Click += (_, _) => SetLanguage(null);
        _langChips.Add((null, allChip));
        langWrap.Children.Add(allChip);

        foreach (var language in _vm.Data.Languages)
        {
            var id = language.Id;
            var chip = Chip($"{language.Name} · {_vm.Data.Words.Count(w => w.LangId == id)}");
            chip.IsChecked = settings.LanguageId == id;
            chip.Click += (_, _) => SetLanguage(id);
            _langChips.Add((id, chip));
            langWrap.Children.Add(chip);
        }
        langPanel.Children.Add(langWrap);
        root.Children.Add(langPanel);

        // ---- 日期
        var datePanel = new StackPanel { Spacing = 6 };
        datePanel.Children.Add(SectionLabel("By date"));
        var dateWrap = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        var allDates = _vm.Data.Words
            .Where(w => w.Date.Length == 10)
            .Select(w => w.Date)
            .Distinct()
            .OrderDescending()
            .ToList();
        foreach (var date in allDates)
        {
            var key = date;
            var chip = Chip(DateChipLabel(key, CountOnDate(key)));
            chip.IsChecked = settings.Dates.Contains(key);
            chip.Click += (_, _) => ToggleDate(key, chip);
            _dateChips[key] = chip;
            dateWrap.Children.Add(chip);
        }
        if (allDates.Count == 0) dateWrap.Children.Add(Hint("No dated words yet."));
        datePanel.Children.Add(new ScrollViewer
        {
            MaxHeight = 132,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = dateWrap,
        });
        datePanel.Children.Add(Hint("Leave empty to include every date."));
        root.Children.Add(datePanel);

        // ---- 分类
        var catPanel = new StackPanel { Spacing = 6 };
        catPanel.Children.Add(SectionLabel("By category"));
        var catWrap = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        var categories = _vm.Data.Categories
            .OrderBy(c => c.Name, StringComparer.CurrentCulture)
            .ToList();
        foreach (var category in categories)
        {
            var id = category.Id;
            var chip = Chip(CategoryLabel(category, CountInCategory(id)));
            chip.IsChecked = settings.CategoryIds.Contains(id);
            chip.Click += (_, _) => ToggleCategory(id, chip);
            _catChips[id] = chip;
            catWrap.Children.Add(chip);
        }
        if (categories.Count == 0) catWrap.Children.Add(Hint("No categories yet."));
        catPanel.Children.Add(new ScrollViewer
        {
            MaxHeight = 132,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = catWrap,
        });
        catPanel.Children.Add(Hint("Leave empty to include every category."));
        root.Children.Add(catPanel);

        // ---- 刷新间隔
        var refreshPanel = new StackPanel { Spacing = 6 };
        refreshPanel.Children.Add(SectionLabel("Refresh time"));
        var refreshWrap = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        var refreshChips = new List<ToggleButton>();
        foreach (var minutes in new[] { 1, 3, 5 })
        {
            var value = minutes;
            var chip = Chip($"{minutes} min");
            chip.IsChecked = settings.RefreshMinutes == value;
            chip.Click += (_, _) =>
            {
                _vm.SaveScreenCard(s => s.RefreshMinutes = value);
                foreach (var other in refreshChips) other.IsChecked = other == chip;
                RestartTimer();
                UpdateStatus();
            };
            refreshChips.Add(chip);
            refreshWrap.Children.Add(chip);
        }
        refreshPanel.Children.Add(refreshWrap);
        root.Children.Add(refreshPanel);

        // ---- 底部统计
        _poolLabel = new TextBlock
        {
            Style = StyleOf("CardMuted"),
            FontSize = 11,
            Text = PoolSummary(),
        };
        root.Children.Add(_poolLabel);

        RefreshFilterChips();
        return root;
    }

    static string DateChipLabel(string date, int count)
    {
        var label = DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed.ToString(parsed.Year == DateTime.Today.Year ? "d MMM" : "d MMM yyyy", En)
            : date;
        return $"{label} · {count}";
    }

    int CountOnDate(string date) => _vm.Data.Words.Count(w =>
        w.Date == date && (_vm.ScreenCard.LanguageId is null || w.LangId == _vm.ScreenCard.LanguageId));

    int CountInCategory(string categoryId) => _vm.Data.Words.Count(w =>
        w.CategoryIds.Contains(categoryId)
        && (_vm.ScreenCard.LanguageId is null || w.LangId == _vm.ScreenCard.LanguageId));

    /// <summary>不同语言可以有同名分类（两边都有 Adjectives），全语言视图里补上语言名前缀。</summary>
    string CategoryLabel(Category category, int count)
    {
        var ambiguous = _vm.ScreenCard.LanguageId is null
            && _vm.Data.Categories.Any(c => c.Id != category.Id && c.Name == category.Name);
        if (!ambiguous) return $"{category.Name} · {count}";

        var language = _vm.Data.Languages.FirstOrDefault(l => l.Id == category.LangId)?.Name ?? "";
        return $"{language} · {category.Name} · {count}";
    }

    void SetLanguage(string? languageId)
    {
        _vm.SaveScreenCard(s =>
        {
            s.LanguageId = languageId;

            var dates = _vm.Data.Words
                .Where(w => languageId is null || w.LangId == languageId)
                .Select(w => w.Date)
                .ToHashSet();
            s.Dates.RemoveAll(d => !dates.Contains(d));

            var categories = _vm.Data.Categories
                .Where(c => languageId is null || c.LangId == languageId)
                .Select(c => c.Id)
                .ToHashSet();
            s.CategoryIds.RemoveAll(c => !categories.Contains(c));
        });

        foreach (var (id, chip) in _langChips) chip.IsChecked = id == languageId;
        RefreshFilterChips();
        OnFilterChanged();
    }

    void ToggleDate(string date, ToggleButton chip)
    {
        _vm.SaveScreenCard(s =>
        {
            if (!s.Dates.Remove(date)) s.Dates.Add(date);
        });
        chip.IsChecked = _vm.ScreenCard.Dates.Contains(date);
        OnFilterChanged();
    }

    void ToggleCategory(string categoryId, ToggleButton chip)
    {
        _vm.SaveScreenCard(s =>
        {
            if (!s.CategoryIds.Remove(categoryId)) s.CategoryIds.Add(categoryId);
        });
        chip.IsChecked = _vm.ScreenCard.CategoryIds.Contains(categoryId);
        OnFilterChanged();
    }

    /// <summary>语言一变，日期/分类的可用性与数量都要跟着重算，不能只重画当前页。</summary>
    void RefreshFilterChips()
    {
        var settings = _vm.ScreenCard;

        foreach (var (date, chip) in _dateChips)
        {
            var count = CountOnDate(date);
            chip.Content = DateChipLabel(date, count);
            chip.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            chip.IsChecked = settings.Dates.Contains(date);
        }

        foreach (var (id, chip) in _catChips)
        {
            var category = _vm.Data.Categories.FirstOrDefault(c => c.Id == id);
            var count = CountInCategory(id);
            var available = category is not null
                && (settings.LanguageId is null || category.LangId == settings.LanguageId)
                && count > 0;
            chip.Content = category is null
                ? $"{count}"
                : CategoryLabel(category, count);
            chip.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            chip.IsChecked = settings.CategoryIds.Contains(id);
        }
    }

    void OnFilterChanged()
    {
        _queue = BuildPool();
        _queueIndex = 0;
        ShowNextCard();
        if (_poolLabel is not null) _poolLabel.Text = PoolSummary();
    }

    string PoolSummary()
    {
        var count = CountPool();
        return count == 1 ? "1 word in rotation" : $"{count:N0} words in rotation";
    }

    // ------------------------------------------------------------ 生命周期

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppViewModel.Theme))
        {
            Root.RequestedTheme = _vm.Theme switch
            {
                "light" => ElementTheme.Light,
                "dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
        else if (e.PropertyName == nameof(AppViewModel.ScreenCardImagePath))
        {
            UpdateCollapsedVisual();
        }
    }

    void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (!_collapsed) SavePosition();
        Close();
    }

    void OnCloseEnter(object sender, PointerRoutedEventArgs e) => CloseButton.Foreground = _glyphShown;

    void OnCloseExit(object sender, PointerRoutedEventArgs e) => CloseButton.Foreground = _glyphHidden;

    void OnClosed(object sender, WindowEventArgs args)
    {
        _timer.Stop();
        _vm.PropertyChanged -= OnVmChanged;
        AppWindow.Changed -= OnAppWindowChanged;
    }
}
