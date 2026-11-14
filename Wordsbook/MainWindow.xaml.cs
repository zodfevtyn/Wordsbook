using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Wordsbook.Models;
using Wordsbook.Services;
using Wordsbook.ViewModels;

namespace Wordsbook;

public sealed partial class MainWindow : Window
{
    const double BaseWindowWidth = 1240;
    const double BaseWindowHeight = 820;

    readonly Store _store;
    readonly IntPtr _hwnd;
    readonly SolidColorBrush _glyphHidden = new(Windows.UI.Color.FromArgb(0x00, 0x00, 0x00, 0x00));
    readonly SolidColorBrush _glyphShown = new(Windows.UI.Color.FromArgb(0x88, 0x00, 0x00, 0x00));
    readonly SolidColorBrush _toolActive = new(Windows.UI.Color.FromArgb(0x3A, 0x0A, 0x84, 0xFF));

    bool _zoomReady;

    // 动效只用 Opacity / Transform：合成线程处理，不触发逐帧布局。
    readonly ScaleTransform _settingsScale = new();
    readonly TranslateTransform _settingsShift = new();
    readonly ScaleTransform _editorScale = new();
    readonly TranslateTransform _editorShift = new();
    TranslateTransform? _wordsShift;
    TranslateTransform? _sidebarShift;
    TranslateTransform? _searchShift;
    Storyboard? _settingsSb;
    Storyboard? _editorSb;
    Storyboard? _wordsSb;
    Storyboard? _sidebarSb;
    Storyboard? _searchSb;
    bool _settingsOpen;
    bool _editorOpen;
    bool _searchOpen;
    bool _uiReady;

    public AppViewModel Vm { get; }

    ScreenCardWindow? _screenCard;

    readonly CollectionViewSource _gridGroups;
    readonly CollectionViewSource _rowGroups;

    public MainWindow(Store store)
    {
        _store = store;
        Vm = new AppViewModel(store, DispatcherQueue.GetForCurrentThread());

        InitializeComponent();

        InitModalMotion(Settings, _settingsScale, _settingsShift);
        InitModalMotion(Editor, _editorScale, _editorShift);
        _wordsShift = UseTranslate(WordsHost);
        _sidebarShift = UseTranslate(SidebarContent);
        _searchShift = UseTranslate(SearchHost);

        // 单词区按日期分组：ListView 只认 ICollectionView，分组源在这里挂一次。
        // 两个 ListView 各用一份视图，切换布局时只改可见性，不碰 ItemsPanel（会崩溃）。
        _gridGroups = new CollectionViewSource { IsSourceGrouped = true, Source = Vm.WordGroups };
        _rowGroups = new CollectionViewSource { IsSourceGrouped = true, Source = Vm.WordGroups };
        WordList.ItemsSource = _gridGroups.View;
        RowList.ItemsSource = _rowGroups.View;
        ApplyLayout();

        Title = "Wordsbook";
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragBar);
        WindowChrome.MakeBorderless(_hwnd);

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        ResizeWindowForZoom();
        ApplyResizeRegions();
        AppWindow.Changed += OnAppWindowChanged;

        Vm.PropertyChanged += OnVmChanged;
        Heat.StepRequested = Vm.StepHeat;
        Editor.Cancelled += CloseEditor;
        Editor.CategoryRequested += AddCategoryFromEditorAsync;
        // 弹窗高度跟窗口走，窗口矮的时候让内容滚动而不是把底部圆角顶出屏幕。
        EditorScrim.SizeChanged += (_, e) => Editor.MaxHeight = Math.Max(240, e.NewSize.Height - 48);
        Editor.Saved += entry =>
        {
            Vm.Save(entry);
            CloseEditor();
        };
        Settings.Changed += ApplySettings;
        Settings.CloseRequested += CloseSettings;
        Settings.ScreenCardImagePickRequested += PickScreenCardImageAsync;
        Settings.ScreenCardImageClearRequested += ClearScreenCardImage;
        // 主窗口一关，置顶小窗会替应用续命，所以必须一起收掉。
        Closed += (_, _) => _screenCard?.Close();

        ApplyTheme();
        UpdateLayoutButton();
        UpdateToolStates();
        Vm.Initialize();
        Vm.SetSidebarOpen(Vm.IsSidebarOpen, animate: false);
        RootGrid.LayoutUpdated += OnRootLayoutUpdated;
    }

    // ------------------------------------------------------------ 缩放 / 主题

    double Zoom => Vm.Scale switch { 1.5 => 1.5, 1.25 => 1.25, _ => 1 };

    double _appliedZoom = -1;

    void ApplySettings()
    {
        ApplyTheme();
        // 只有缩放真的变了才动窗口尺寸：改主题时也 Resize 会让窗口跳到另一块屏幕。
        if (Zoom == _appliedZoom) ApplyZoom();
        else ResizeWindowForZoom();
    }

    void ApplyTheme()
    {
        RootGrid.RequestedTheme = ThemeSetting;
    }

    ElementTheme ThemeSetting => Vm.Theme switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>
    /// 对话框主按钮默认吃系统强调色（这台机器是橙色），换成应用自己的蓝。
    /// 主题本身由每个对话框初始化时的 RequestedTheme 负责：弹层不吃 RootGrid 的，
    /// 而这个 SDK 的 XamlRoot 又没有 RequestedTheme 可设。
    /// </summary>
    /// <summary>ThemeDictionaries 的键不一定能从 Resources 索引器取到，取不到就退回写死的备用色。</summary>
    Brush ThemeBrush(string key, string fallbackHex)
    {
        if (Application.Current.Resources[key] is Brush b) return b;
        var h = fallbackHex.TrimStart((char)35);
        byte P(int i) => (byte)Convert.ToInt32(h.Substring(i, 2), 16);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(P(0), P(2), P(4), P(6)));
    }

    void TintPrimary(ContentDialog dialog)
    {
        var accent = ThemeBrush("Accent", "#FF0A7CFF");
        var white = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        foreach (var key in new[]
        {
            "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed",
            "AccentButtonBorderBrush",
        }) dialog.Resources[key] = accent;
        foreach (var key in new[]
        {
            "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed",
        }) dialog.Resources[key] = white;
    }

    /// <summary>窗口按物理像素给足尺寸，内容再按缩放倍率反向铺满，等效于整套 UI 放大。</summary>
    void ResizeWindowForZoom()
    {
        _appliedZoom = Zoom;
        var z = Zoom * WindowChrome.DpiScale(_hwnd);
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

        // 1.5x 下 1240x820 会变成 1860x1230，1080p 屏装不下，弹窗底部就会被推到屏幕外。
        const int gap = 24;
        var w = Math.Min((int)(BaseWindowWidth * z), area.Width - gap * 2);
        var h = Math.Min((int)(BaseWindowHeight * z), area.Height - gap * 2);
        var pos = AppWindow.Position;
        var x = Math.Clamp(pos.X, area.X + gap, Math.Max(area.X + gap, area.X + area.Width - gap - w));
        var y = Math.Clamp(pos.Y, area.Y + gap, Math.Max(area.Y + gap, area.Y + area.Height - gap - h));

        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));
        ApplyZoom();
    }

    void ApplyZoom()
    {
        var z = Zoom;
        var w = ZoomHost.ActualWidth;
        var h = ZoomHost.ActualHeight;
        if (w < 2 || h < 2) return;

        RootGrid.Width = w / z;
        RootGrid.Height = h / z;
        RootScale.ScaleX = z;
        RootScale.ScaleY = z;

        // 贴边面板的 18 圆角是 RootGrid 坐标系里的，换成屏幕物理像素还要再乘缩放和 DPI，
        // 窗口区域必须切在同一个半径上，否则圆角外面会露出一圈黑底。
        RequestIslandAlign();
        WindowChrome.CornerPhysical = WindowCorner * z * WindowChrome.DpiScale(_hwnd);
        WindowChrome.ApplyCornerRegion(_hwnd);
        ApplyCornerRadii(CoversWorkArea() ? 0 : WindowCorner);
    }

    DispatcherQueueTimer? _alignTimer;
    double _alignDpi = -1;

    /// <summary>
    /// 抬岛要推迟，而且刚换过 DPI 的那一次必须跳过。当场抬不行：WM_SIZE 里
    /// DesktopChildSiteBridge 正被 WinUI 重建，SetWindowPos 把它挪走之后合成目标和布局
    /// 就脱钩，整页字形被撕成半截（实测搬到另一块不同缩放的屏上必现，留着 (0,1) 不动就正常）。
    /// 所以 DPI 一变先只记下新值、放过这一次，等同一个 DPI 下的下一次尺寸变更再补抬；
    /// 那 150ms 也顺便让 WinUI 走完自己的重建。AlignIsland 只在真的偏了才下发，反复触发没有副作用。
    /// </summary>
    void RequestIslandAlign()
    {
        if (_alignTimer is null)
        {
            _alignTimer = DispatcherQueue.CreateTimer();
            _alignTimer.Tick += (_, _) => AlignIslandNow();
        }
        _alignTimer.Interval = TimeSpan.FromMilliseconds(150);
        _alignTimer.IsRepeating = false;
        _alignTimer.Stop();
        _alignTimer.Start();
    }

    void AlignIslandNow()
    {
        var dpi = WindowChrome.DpiScale(_hwnd);
        var changed = _alignDpi >= 0 && Math.Abs(dpi - _alignDpi) > 0.001;
        _alignDpi = dpi;
        if (!changed) WindowChrome.AlignIsland(_hwnd);
    }

    const double WindowCorner = 18;
    double _cornerRadii = -1;

    /// <summary>点绿灯走的是 AppWindow.MoveAndResize，IsZoomed 不会变真，只能自己比工作区。</summary>
    bool CoversWorkArea() => WindowChrome.CoversWorkArea(_hwnd);

    /// <summary>铺满工作区时四角就是屏幕角，贴边的几个面得跟着改回直角。</summary>
    void ApplyCornerRadii(double r)
    {
        if (Math.Abs(r - _cornerRadii) < 0.5) return;
        _cornerRadii = r;

        var left = new CornerRadius(r, 0, 0, r);
        var right = new CornerRadius(0, r, r, 0);
        var topRight = new CornerRadius(0, r, 0, 0);
        var all = new CornerRadius(r);

        SidebarSurface.CornerRadius = left;
        SidebarGlass.CornerRadius = left;
        SidebarVeil.CornerRadius = left;
        ContentSurface.CornerRadius = right;
        HeaderGlass.CornerRadius = topRight;
        HeaderVeilBorder.CornerRadius = topRight;
        EditorScrim.CornerRadius = all;
        SettingsScrim.CornerRadius = all;
    }

    void OnZoomHostResized(object sender, SizeChangedEventArgs e) => ApplyZoom();

    /// <summary>
    /// 客户区铺满整个窗口之后 DWM 不再给四边留缩放热区，必须自己把边缘注册成
    /// 非客户区，否则拖边只会变成选中文本。顶边故意留给 DragBar 拖动，不注册。
    /// </summary>
    void ApplyResizeRegions()
    {
        var src = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        var b = WindowChrome.ResizeBorder(_hwnd);
        var (w, h) = (AppWindow.Size.Width, AppWindow.Size.Height);

        static Windows.Graphics.RectInt32 R(int x, int y, int cw, int ch) => new(x, y, cw, ch);

        src.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.LeftBorder, new[] { R(0, 0, b, h) });
        src.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.RightBorder, new[] { R(w - b, 0, b, h) });
        src.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.TopBorder, new[] { R(b, 0, w - 2 * b, b) });
        src.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.BottomBorder, new[] { R(b, h - b, w - 2 * b, b) });

        ApplyCaptionHoles();
    }

    /// <summary>
    /// 把标题栏里的控件从 caption 命中区里挖出来。DragBar 整条都是系统标题栏，
    /// 落在里面的控件收不到鼠标：红绿灯和侧栏按钮点不动，按钮条的缝隙还会被当成
    /// 标题栏双击而触发最大化。Passthrough 要的是物理像素、相对客户区的矩形。
    /// </summary>
    void ApplyCaptionHoles()
    {
        var rects = new List<Windows.Graphics.RectInt32>();
        foreach (var e in new FrameworkElement[] { TrafficLights, SidebarToggleButton, ToolButtons })
        {
            if (e.ActualWidth < 2 || e.ActualHeight < 2) continue;
            var b = e.TransformToVisual(null).TransformBounds(
                new Windows.Foundation.Rect(0, 0, e.ActualWidth, e.ActualHeight));
            rects.Add(new Windows.Graphics.RectInt32(
                (int)Math.Floor(b.X * _captionScale),
                (int)Math.Floor(b.Y * _captionScale),
                (int)Math.Ceiling(b.Width * _captionScale),
                (int)Math.Ceiling(b.Height * _captionScale)));
        }
        if (rects.Count == 0) return;

        // LayoutUpdated 每帧都会跑，动画期间这些矩形其实没动，比对一下再决定要不要下发。
        // RectInt32 没有重写 ToString，拼字符串必须自己取值，否则去重永远命中。
        var key = string.Join(" ", rects.ConvertAll(r => $"{r.X},{r.Y} {r.Width}x{r.Height}"));
        if (key == _captionRects) return;
        _captionRects = key;

        Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(AppWindow.Id)
            .SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough, rects.ToArray());
    }

    string _captionRects = "";

    double _captionScale => WindowChrome.DpiScale(_hwnd);

    void OnRootLayoutUpdated(object? sender, object e) => ApplyCaptionHoles();

    void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange || args.DidPositionChange) RequestIslandAlign();
        if (args.DidSizeChange) ApplyResizeRegions();
        var maxed = sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        TrafficMax.Content = maxed ? Icons.Shrink : Icons.Expand;
    }

    void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppViewModel.HeatCells):
            case nameof(AppViewModel.HeatToday):
                Heat.Render(Vm.HeatCells, Vm.HeatToday);
                break;
            case nameof(AppViewModel.HeatLabel):
                Heat.Month = Vm.HeatLabel;
                break;
            case nameof(AppViewModel.IsSidebarOpen):
                BeginSidebarFreeze();
                if (_sidebarFreeze && SidebarAtRest()) EndSidebarFreeze();
                AnimateSidebarContent(Vm.IsSidebarOpen);
                break;
            case nameof(AppViewModel.SidebarWidth):
                if (_sidebarFreeze)
                {
                    // 先把新列宽落到布局，再补偿平移；否则补偿会跑在列宽前面，卡片先向反方向甩出去。
                    RootGrid.UpdateLayout();
                    if (_wordsShift is not null)
                        _wordsShift.X = _freezeStartTranslateX + (_freezeStartSidebarWidth - Vm.SidebarWidth);
                    if (SidebarAtRest()) EndSidebarFreeze();
                }
                break;
        }
    }

    void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (_zoomReady) return;
        _zoomReady = true;
        _uiReady = true;
        ApplyZoom();
        Heat.Render(Vm.HeatCells, Vm.HeatToday);
        Heat.Month = Vm.HeatLabel;

        if (Environment.GetEnvironmentVariable("WORDBOOK_GEOM") is string geomPath)
        {
            var t = DispatcherQueue.CreateTimer();
            t.Interval = TimeSpan.FromMilliseconds(1500);
            t.IsRepeating = false;
            t.Tick += (_, _) => WindowChrome.DumpGeometry(
                _hwnd, geomPath);
            t.Start();
        }
    }

    // ------------------------------------------------------------ 红绿灯

    void OnTrafficEntered(object sender, PointerRoutedEventArgs e)
    {
        TrafficClose.Foreground = _glyphShown;
        TrafficMin.Foreground = _glyphShown;
        TrafficMax.Foreground = _glyphShown;
    }

    void OnTrafficExited(object sender, PointerRoutedEventArgs e)
    {
        TrafficClose.Foreground = _glyphHidden;
        TrafficMin.Foreground = _glyphHidden;
        TrafficMax.Foreground = _glyphHidden;
    }

    void OnTrafficClose(object sender, RoutedEventArgs e) => Close();

    void OnTrafficMinimize(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter p) p.Minimize();
    }

    void OnTrafficMaximize(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is not OverlappedPresenter p) return;
        if (p.State == OverlappedPresenterState.Maximized) p.Restore();
        else p.Maximize();
    }

    // ------------------------------------------------------------ 动效

    bool _sidebarFreeze;
    bool _wordsDeferred;
    double _freezeStartSidebarWidth;
    double _freezeStartTranslateX;

    bool SidebarAtRest()
    {
        var target = Vm.IsSidebarOpen ? Vm.SidebarOpenWidth : 0;
        return Math.Abs(Vm.SidebarWidth - target) < 0.5;
    }

    /// <summary>
    /// 侧栏宽度动画期间只改列宽，不再让两侧内容跟着重新测量：
    /// 把宽度锁在 WordsHost 自己身上（内层视图随它拉伸），这样布局边界和裁剪边界都停在起点，
    /// 再用反向平移钉住原位，卡片在侧栏动画期间既不重排也不被列宽啃掉右侧一条空白；
    /// 侧栏落定后才释放宽度重排并单独过渡。
    /// </summary>
    void BeginSidebarFreeze()
    {
        if (!_uiReady || _sidebarFreeze) return;
        _sidebarFreeze = true;
        _wordsDeferred = false;

        _wordsSb?.Stop();
        ResetWordsMotion();
        _freezeStartSidebarWidth = Vm.SidebarWidth;
        _freezeStartTranslateX = 0;

        var hostWidth = WordsHost.ActualWidth;
        if (hostWidth < 1) hostWidth = Math.Max(0,
            RootGrid.ActualWidth - Vm.SidebarOpenWidth - WordsHost.Margin.Left - WordsHost.Margin.Right);
        WordsHost.Width = hostWidth;
        WordsHost.HorizontalAlignment = HorizontalAlignment.Left;

        SidebarScrollContent.Width = Math.Max(0,
            Vm.SidebarOpenWidth - SidebarScroll.Padding.Left - SidebarScroll.Padding.Right);
    }

    void EndSidebarFreeze()
    {
        if (!_sidebarFreeze) return;
        _sidebarFreeze = false;
        var fromX = _wordsShift?.X ?? 0;
        SidebarScrollContent.ClearValue(FrameworkElement.WidthProperty);

        if (fromX > 0.5)
        {
            // 收起：右侧还有整屏宽度可退，先释放宽度再让卡片从右边滑回，看不到重排硬跳。
            ReleaseWordLocks();
            SettleWordsAfterSidebar(fromX);
            return;
        }

        // 展开：卡片没有地方可滑（往右滑会在窗口右侧留空白），所以先把钉住的表淡干净，
        // 在全透明状态下一次性重排，再淡入。
        _wordsDeferred = true;
        _wordsSb?.Stop();
        var outSb = new Storyboard();
        outSb.Children.Add(Motion(WordsHost, "Opacity", WordsHost.Opacity, 0, 90, InCubic()));
        _wordsSb = outSb;
        outSb.Completed += (_, _) =>
        {
            if (!_wordsDeferred) return;
            _wordsDeferred = false;
            ReleaseWordLocks();
            SettleWordsAfterSidebar(0);
        };
        outSb.Begin();
    }

    void ReleaseWordLocks()
    {
        WordsHost.ClearValue(FrameworkElement.WidthProperty);
        WordsHost.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
    }

    /// <summary>侧栏落定后的第二阶段：收起时卡片从右侧滑回，展开时在全透明下重排后淡入。</summary>
    void SettleWordsAfterSidebar(double fromX)
    {
        if (_wordsShift is null) return;
        _wordsSb?.Stop();
        var dim = fromX > 0.5 ? 0.8 : 0;
        WordsHost.Opacity = dim;
        _wordsShift.Y = 7;
        var ease = OutCubic();
        var sb = new Storyboard();
        sb.Children.Add(Motion(WordsHost, "Opacity", dim, 1, dim > 0 ? 220 : 190, ease));
        sb.Children.Add(Motion(_wordsShift, "Y", 7, 0, 240, ease));
        if (fromX > 0.5)
            sb.Children.Add(Motion(_wordsShift, "X", fromX, 0, 240, ease));
        else
            _wordsShift.X = 0;
        _wordsSb = sb;
        sb.Completed += (_, _) =>
        {
            // Storyboard 结束后把基底写成终值，否则下一次 Stop() 会跳回旧的 X。
            if (ReferenceEquals(_wordsSb, sb)) ResetWordsMotion();
        };
        sb.Begin();
    }

    void ResetWordsMotion()
    {
        if (_wordsShift is null) return;
        _wordsShift.X = 0;
        _wordsShift.Y = 0;
        WordsHost.Opacity = 1;
    }

    static TranslateTransform UseTranslate(UIElement el)
    {
        if (el.RenderTransform is TranslateTransform t) return t;
        t = new TranslateTransform();
        el.RenderTransform = t;
        return t;
    }

    static void InitModalMotion(FrameworkElement el, ScaleTransform scale, TranslateTransform shift)
    {
        el.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(shift);
        el.RenderTransform = group;
    }

    static DoubleAnimation Motion(DependencyObject target, string property, double from, double to,
        double ms, EasingFunctionBase ease)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(ms)),
            EasingFunction = ease,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    static CubicEase OutCubic() => new() { EasingMode = EasingMode.EaseOut };

    static CubicEase InCubic() => new() { EasingMode = EasingMode.EaseIn };

    void ModalIn(FrameworkElement scrim, ScaleTransform scale, TranslateTransform shift, ref Storyboard? storyboard)
    {
        storyboard?.Stop();
        scrim.Visibility = Visibility.Visible;
        scrim.Opacity = 0;
        scale.ScaleX = scale.ScaleY = 0.965;
        shift.Y = 10;
        var ease = OutCubic();
        var sb = new Storyboard();
        sb.Children.Add(Motion(scrim, "Opacity", 0, 1, 180, ease));
        sb.Children.Add(Motion(scale, "ScaleX", 0.965, 1, 240, ease));
        sb.Children.Add(Motion(scale, "ScaleY", 0.965, 1, 240, ease));
        sb.Children.Add(Motion(shift, "Y", 10, 0, 240, ease));
        storyboard = sb;
        sb.Begin();
    }

    void ModalOut(FrameworkElement scrim, ScaleTransform scale, TranslateTransform shift,
        ref Storyboard? storyboard, Func<bool> stillOpen)
    {
        storyboard?.Stop();
        scrim.Opacity = 1;
        scale.ScaleX = scale.ScaleY = 1;
        shift.Y = 0;
        var ease = InCubic();
        var sb = new Storyboard();
        sb.Children.Add(Motion(scrim, "Opacity", 1, 0, 130, ease));
        sb.Children.Add(Motion(scale, "ScaleX", 1, 0.982, 150, ease));
        sb.Children.Add(Motion(scale, "ScaleY", 1, 0.982, 150, ease));
        sb.Children.Add(Motion(shift, "Y", 0, 6, 150, ease));
        sb.Completed += (_, _) =>
        {
            if (stillOpen()) return;
            scrim.Visibility = Visibility.Collapsed;
        };
        storyboard = sb;
        sb.Begin();
    }

    void AnimateWordsIn()
    {
        if (_wordsShift is null) return;
        _wordsSb?.Stop();
        ResetWordsMotion();
        WordsHost.Opacity = 0.55;
        _wordsShift.Y = 7;
        var ease = OutCubic();
        var sb = new Storyboard();
        sb.Children.Add(Motion(WordsHost, "Opacity", 0.55, 1, 200, ease));
        sb.Children.Add(Motion(_wordsShift, "Y", 7, 0, 240, ease));
        _wordsSb = sb;
        sb.Begin();
    }

    void AnimateSidebarContent(bool open)
    {
        if (!_uiReady || _sidebarShift is null) return;
        _sidebarSb?.Stop();
        var ms = open ? 240 : 190;
        var ease = open ? (EasingFunctionBase)OutCubic() : InCubic();
        SidebarContent.Opacity = open ? 0.35 : 1;
        _sidebarShift.X = open ? -10 : 0;
        var sb = new Storyboard();
        sb.Children.Add(Motion(SidebarContent, "Opacity", open ? 0.35 : 1, open ? 1 : 0.15, ms, ease));
        sb.Children.Add(Motion(_sidebarShift, "X", open ? -10 : 0, open ? 0 : -10, ms, ease));
        _sidebarSb = sb;
        sb.Begin();
    }

    void AnimateSearchHost(bool show)
    {
        if (_searchShift is null) return;
        _searchSb?.Stop();
        if (show)
        {
            _searchOpen = true;
            SearchHost.Visibility = Visibility.Visible;
            SearchHost.Opacity = 0;
            _searchShift.X = 8;
            var ease = OutCubic();
            var sb = new Storyboard();
            sb.Children.Add(Motion(SearchHost, "Opacity", 0, 1, 180, ease));
            sb.Children.Add(Motion(_searchShift, "X", 8, 0, 200, ease));
            _searchSb = sb;
            sb.Begin();
            SearchBox.Focus(FocusState.Programmatic);
        }
        else
        {
            _searchOpen = false;
            SearchHost.Opacity = 1;
            _searchShift.X = 0;
            var ease = InCubic();
            var sb = new Storyboard();
            sb.Children.Add(Motion(SearchHost, "Opacity", 1, 0, 120, ease));
            sb.Children.Add(Motion(_searchShift, "X", 0, 6, 140, ease));
            sb.Completed += (_, _) =>
            {
                if (_searchOpen) return;
                SearchHost.Visibility = Visibility.Collapsed;
            };
            _searchSb = sb;
            sb.Begin();
        }
    }

    static void AnimateAddTile(FrameworkElement tile, bool enter)
    {
        var animation = new DoubleAnimation
        {
            To = enter ? 1 : 0.55,
            Duration = new Duration(TimeSpan.FromMilliseconds(enter ? 120 : 190)),
            EasingFunction = enter
                ? new CubicEase { EasingMode = EasingMode.EaseOut }
                : new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(animation, tile);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(animation);
        sb.Begin();
    }

    // ------------------------------------------------------------ 侧栏

    void OnToggleSidebar(object sender, RoutedEventArgs e) => Vm.ToggleSidebar();

    readonly SolidColorBrush _splitterHot = new(Windows.UI.Color.FromArgb(0x33, 0x0A, 0x7C, 0xFF));
    double _splitStartX;
    double _splitStartWidth;
    bool _splitting;
    bool _splitterHover;

    void RefreshSplitterTint() =>
        SidebarSplitter.Background = _splitting || _splitterHover ? _splitterHot : null;

    void OnSplitterEnter(object sender, PointerRoutedEventArgs e)
    {
        _splitterHover = true;
        RefreshSplitterTint();
    }

    void OnSplitterExit(object sender, PointerRoutedEventArgs e)
    {
        _splitterHover = false;
        RefreshSplitterTint();
    }

    void OnSplitterPressed(object sender, PointerRoutedEventArgs e)
    {
        // RootGrid 坐标：外面套了缩放变换，GetCurrentPoint 已经把倍率折算掉
        _splitStartX = e.GetCurrentPoint(RootGrid).Position.X;
        _splitStartWidth = Vm.SidebarWidth;
        _splitting = true;
        RefreshSplitterTint();
        SidebarSplitter.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    // 挂在 RootGrid 上而不是分隔条上：万一指针捕获被别处抢走，移动事件仍然能冒泡上来
    void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_splitting) return;
        var x = e.GetCurrentPoint(RootGrid).Position.X;
        Vm.SetSidebarWidth(_splitStartWidth + (x - _splitStartX));
    }

    void OnRootPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_splitting) return;
        _splitting = false;
        SidebarSplitter.ReleasePointerCapture(e.Pointer);
        RefreshSplitterTint();
    }

    void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        Settings.SyncFrom(Vm);
        _settingsOpen = true;
        ModalIn(SettingsScrim, _settingsScale, _settingsShift, ref _settingsSb);
    }

    void CloseSettings()
    {
        if (!_settingsOpen) return;
        _settingsOpen = false;
        ModalOut(SettingsScrim, _settingsScale, _settingsShift, ref _settingsSb, () => _settingsOpen);
    }

    void OnSettingsScrimTapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource)) CloseSettings();
    }

    // ------------------------------------------------------------ 置顶单词卡

    void OnOpenScreenCard(object sender, RoutedEventArgs e)
    {
        if (_screenCard is null)
        {
            _screenCard = new ScreenCardWindow(Vm);
            _screenCard.Closed += (_, _) =>
            {
                _screenCard = null;
                UpdateToolStates();
            };
        }

        _screenCard.Activate();
        UpdateToolStates();
    }

    async void PickScreenCardImageAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" })
            picker.FileTypeFilter.Add(extension);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        Vm.SetScreenCardImage(file.Path);
        Settings.SyncFrom(Vm);
    }

    void ClearScreenCardImage()
    {
        Vm.SetScreenCardImage(null);
        Settings.SyncFrom(Vm);
    }

    void OnStartToday(object sender, RoutedEventArgs e)
    {
        Vm.StartToday();
        AnimateWordsIn();
    }

    async void OnAddCategory(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedLangId is not { } langId) return;
        await AddCategoryAsync(langId);
    }

    async Task<Category?> AddCategoryFromEditorAsync(string langId) => await AddCategoryAsync(langId);

    async Task<Category?> AddCategoryAsync(string langId)
    {
        if (langId.Length == 0) return null;

        var input = new TextBox { PlaceholderText = "Category name" };
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = ThemeSetting,
            Title = "New category",
            Content = new Border { Padding = new Thickness(0, 8, 0, 0), Child = input },
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        TintPrimary(dialog);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var name = input.Text.Trim();
        if (name.Length == 0) return null;

        var existing = Vm.CategoriesOf(langId)
            .FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return existing ?? Vm.AddCategory(langId, name);
    }

    void OnEditLanguages(object sender, RoutedEventArgs e)
    {
        var node = Vm.LangNodes.FirstOrDefault(n => n.Id == Vm.SelectedLangId) ?? Vm.LangNodes.FirstOrDefault();
        var anchor = (FrameworkElement)sender;
        ShowLanguageMenu(node, anchor, new Windows.Foundation.Point(0, anchor.ActualHeight));
    }

    void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            Vm.SelectLanguage(id);
            AnimateWordsIn();
        }
    }

    void OnLanguageChevron(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) Vm.ToggleExpanded(id);
    }

    void OnMonthClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            Vm.SelectMonth(key);
            AnimateWordsIn();
        }
    }

    void OnMonthChevron(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key }) Vm.ToggleExpanded(key);
    }

    void OnDayClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            Vm.SelectDay(key);
            AnimateWordsIn();
        }
    }

    void OnCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            Vm.SelectCategory(id);
            AnimateWordsIn();
        }
    }

    void OnCategoryGroupChevron(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) Vm.ToggleExpanded("cat:" + id);
    }

    void OnLanguageRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: LangNode node }) return;
        ShowLanguageMenu(node, (FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
    }

    /// <summary>代码里现建的图标：FontIcon 默认吃系统符号字体，这里要显式指到 SF Pro 的符号区。</summary>
    static FontIcon Glyph(string code) => new() { Glyph = code, FontFamily = Icons.Font };

    void ShowLanguageMenu(LangNode? node, FrameworkElement anchor, Windows.Foundation.Point position)
    {
        var menu = new MenuFlyout();

        var edit = new MenuFlyoutItem { Text = "Rename / change flag", Icon = Glyph(Icons.Edit), IsEnabled = node is not null };
        edit.Click += async (_, _) => await EditLanguageAsync(node);

        var add = new MenuFlyoutItem { Text = "New language", Icon = Glyph(Icons.Plus) };
        add.Click += async (_, _) => await EditLanguageAsync(null);

        var remove = new MenuFlyoutItem { Text = "Delete language", Icon = Glyph(Icons.Delete), IsEnabled = node is not null };
        remove.Click += async (_, _) =>
        {
            if (node is null) return;
            var confirm = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = ThemeSetting,
                Title = $"Delete {node.Name}?",
                Content = "Every word, category and date under this language will be deleted. This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            TintPrimary(confirm);
            if (await confirm.ShowAsync() == ContentDialogResult.Primary) Vm.DeleteLanguage(node.Id);
        };

        menu.Items.Add(edit);
        menu.Items.Add(add);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(remove);
        menu.ShowAt(anchor, position);
    }

    async Task EditLanguageAsync(LangNode? node)
    {
        var lang = node is null ? null : Vm.Data.Languages.FirstOrDefault(l => l.Id == node.Id);

        var nameBox = new TextBox
        {
            Header = "Name",
            Text = lang?.Name ?? "",
            PlaceholderText = "e.g. Nederlands",
        };
        var codeBox = new TextBox
        {
            Header = "Flag code (ISO 3166-1 alpha-2)",
            Text = lang?.Code ?? "",
            PlaceholderText = "e.g. nl",
            Margin = new Thickness(0, 10, 0, 0),
        };

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = ThemeSetting,
            Title = node is null ? "New language" : "Edit language",
            Content = new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    nameBox,
                    codeBox,
                    new TextBlock
                    {
                        Text = "Unlisted codes fall back to a letter badge.",
                        Margin = new Thickness(0, 8, 0, 0),
                        FontSize = 12,
                        Foreground = ThemeBrush("TextSecondary", "#FF86868B"),
                    },
                },
            },
            PrimaryButtonText = node is null ? "Add" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        TintPrimary(dialog);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var name = nameBox.Text.Trim();
        if (name.Length == 0) return;
        var code = codeBox.Text.Trim().ToLowerInvariant();

        if (node is null) Vm.AddLanguage(name, code);
        else Vm.UpdateLanguage(node.Id, name, code);
    }

    // ------------------------------------------------------------ 单词区

    void OnWordItemClick(object sender, ItemClickEventArgs e)
    {
        PanLog($"ItemClick panActive={_rowPanActive}"); // TEMP DIAG
        if (e.ClickedItem is not WordView view || view.IsPlaceholder) return;
        if (view.Model is { } model)
        {
            Editor.OpenEdit(Vm, model);
            ShowEditor(true);
        }
    }

    void OnAdaptiveCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WordView view } || view.IsPlaceholder) return;
        if (view.Model is { } model)
        {
            e.Handled = true;
            Editor.OpenEdit(Vm, model);
            ShowEditor(true);
        }
    }

    void OnCardRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WordView view } || view.IsPlaceholder) return;
        e.Handled = true;

        var menu = new MenuFlyout();

        var edit = new MenuFlyoutItem { Text = "Edit", Icon = Glyph(Icons.Edit) };
        edit.Click += (_, _) =>
        {
            if (view.Model is not null)
            {
                Editor.OpenEdit(Vm, view.Model);
                ShowEditor(true);
            }
        };

        var del = new MenuFlyoutItem { Text = "Delete", Icon = Glyph(Icons.Delete) };
        del.Click += (_, _) =>
        {
            if (view.Model is { } model) Vm.Delete(model);
        };

        menu.Items.Add(edit);
        menu.Items.Add(del);
        menu.ShowAt((FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
    }

    // ------------------------------------------------------------ 列表行横向拖动

    /// <summary>按下后指针走出这个数才开始平移——否则单击选词会被吃掉。</summary>
    const double RowPanThreshold = 4;

    ScrollViewer? _rowPanStrip;
    Windows.Foundation.Point _rowPanAnchor;
    double _rowPanStartOffset;
    bool _rowPanActive;

    // TEMP DIAG：横向拖动不生效，先看是哪个守卫拦下的。
    static void PanLog(string s)
    {
        if (Environment.GetEnvironmentVariable("WORDBOOK_PANLOG") is not string path) return;
        try { System.IO.File.AppendAllText(path, s + "\n"); } catch { }
    }

    /// <summary>
    /// 带子的最小宽度钉在可视宽度上：元素少时中间那列 * 把富余吃掉，排版和以前一样；
    /// 元素多到超过可视宽度时带子按自然宽度铺开，横向滚动随之可用。
    /// 用 SizeChanged 而不是 x:Bind ViewportWidth——模板里的 x:Bind 指到同模板元素
    /// 会让 XamlCompiler 在 Pass2 直接内部崩溃。
    /// </summary>
    void OnRowStripSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ScrollViewer { Content: Grid inner }) inner.MinWidth = e.NewSize.Width;
    }

    /// <summary>
    /// 从命中元素往上找那一行的横向带子。列表自己的纵向 ScrollViewer 的
    /// HorizontalScrollMode 是 Disabled，靠这个区分开。
    /// </summary>
    static ScrollViewer? RowStripAt(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is ScrollViewer { HorizontalScrollMode: ScrollMode.Enabled } sv) return sv;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    /// <summary>
    /// 按下挂在带子的内容 Grid 上（事件从文字往上冒，先经过这里，ListViewItem 还没来得及
    /// 把它标成已处理），移动和抬起挂在 RowList 上。反过来不行：ListViewItem 在 Pressed 里
    /// 既抢指针又把事件标成已处理，带子收不到后续 PointerMoved，列表又收不到那次按下
    /// ——实测两处各挂一半才跑得通。坐标一律以带子为参照，从哪个元素算都一样。
    /// </summary>
    void OnRowStripPanPressed(object sender, PointerRoutedEventArgs e)
    {
        _rowPanStrip = null;
        var found = sender is Grid g ? RowStripAt(g) : null;
        var extent = found is null ? "n/a" : $"{found.ExtentWidth:0.#}/{found.ViewportWidth:0.#}";
        PanLog($"Pressed sender={sender.GetType().Name} strip={(found is null ? "null" : "found")} " +
            $"scrollable={(found?.ScrollableWidth ?? -1):0.#} hsm={found?.HorizontalScrollMode} extent={extent}");
        if (sender is not Grid grid || RowStripAt(grid) is not { } strip || strip.ScrollableWidth <= 0) return;
        if (!e.GetCurrentPoint(strip).Properties.IsLeftButtonPressed) return;

        _rowPanStrip = strip;
        _rowPanAnchor = e.GetCurrentPoint(strip).Position;
        _rowPanStartOffset = strip.HorizontalOffset;
        _rowPanActive = false;
        PanLog($"Pressed scrollable={strip.ScrollableWidth:0.#} anchorX={_rowPanAnchor.X:0.#}");
    }

    void OnRowStripPanMoved(object sender, PointerRoutedEventArgs e)
    {
        // TEMP DIAG：守卫之前先记一行，用来分清「Moved 根本没冒上来」和「上来了但被守卫拦下」。
        PanLog($"Moved enter strip={_rowPanStrip is not null} " +
            $"btn={e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed}");
        if (_rowPanStrip is not ScrollViewer strip) return;
        if (!e.GetCurrentPoint(strip).Properties.IsLeftButtonPressed) return;

        var x = e.GetCurrentPoint(strip).Position.X;
        if (!_rowPanActive && Math.Abs(x - _rowPanAnchor.X) < RowPanThreshold) return;

        _rowPanActive = true;
        var to = _rowPanStartOffset - (x - _rowPanAnchor.X);
        strip.ChangeView(to, null, null, true);
        PanLog($"Moved x={x:0.#} want={to:0.#} actual={strip.HorizontalOffset:0.#} ext={strip.ExtentWidth:0.#} vp={strip.ViewportWidth:0.#}");
    }

    void OnRowStripPanEnded(object sender, PointerRoutedEventArgs e) => _rowPanStrip = null;

    /// <summary>
    /// 拖过的那一下不能再算成点击：ListView 的 ItemClick 由容器的 Tapped 驱动，
    /// 所以在这里把 Tapped 吃掉，松开指针就不会顺手把编辑弹窗打开。
    /// 标记留到这里才清——Pressed 每次都会重置它。
    /// </summary>
    void OnRowStripTapped(object sender, TappedRoutedEventArgs e)
    {
        PanLog($"Tapped active={_rowPanActive} handled->{(_rowPanActive ? "yes" : "no")}");
        if (_rowPanActive) e.Handled = true;
        _rowPanActive = false;
    }

    void OnAddTileTapped(object sender, TappedRoutedEventArgs e)
    {
        if (Vm.Data.Languages.Count == 0) return;
        Editor.OpenNew(Vm);
        ShowEditor(true);
    }

    void OnAddTileEnter(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement tile) AnimateAddTile(tile, true);
    }

    void OnAddTileExit(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement tile) AnimateAddTile(tile, false);
    }

    void ShowEditor(bool visible)
    {
        if (visible)
        {
            _editorOpen = true;
            ModalIn(EditorScrim, _editorScale, _editorShift, ref _editorSb);
        }
        else
        {
            if (!_editorOpen) return;
            _editorOpen = false;
            ModalOut(EditorScrim, _editorScale, _editorShift, ref _editorSb, () => _editorOpen);
        }
    }

    void CloseEditor() => ShowEditor(false);

    void OnScrimTapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource)) CloseEditor();
    }

    void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;

        if (SettingsScrim.Visibility == Visibility.Visible) CloseSettings();
        else if (EditorScrim.Visibility == Visibility.Visible) CloseEditor();
        else return;

        e.Handled = true;
    }

    // ------------------------------------------------------------ 工具栏

    void OnToggleLayout(object sender, RoutedEventArgs e)
    {
        if (Vm.Adaptive) Vm.Adaptive = false;
        Vm.GridView = !Vm.GridView;
        ApplyLayout();
        UpdateLayoutButton();
        UpdateToolStates();
        AnimateWordsIn();
    }

    void OnToggleAdaptive(object sender, RoutedEventArgs e)
    {
        Vm.Adaptive = !Vm.Adaptive;
        ApplyLayout();
        UpdateToolStates();
        AnimateWordsIn();
    }

    void UpdateLayoutButton() => LayoutButton.Content = Vm.GridView ? Icons.List : Icons.Grid;

    void ApplyLayout()
    {
        var adaptive = Vm.Adaptive;
        WordList.Visibility = !adaptive && Vm.GridView ? Visibility.Visible : Visibility.Collapsed;
        RowList.Visibility = !adaptive && !Vm.GridView ? Visibility.Visible : Visibility.Collapsed;
        AdaptiveScroll.Visibility = adaptive ? Visibility.Visible : Visibility.Collapsed;
        // 退出自适应时直接断开数据源，避免隐藏状态下继续生成整批卡片。
        AdaptiveGroups.ItemsSource = adaptive ? Vm.WordGroups : null;
    }

    void OnToggleFocus(object sender, RoutedEventArgs e)
    {
        Vm.FocusMode = !Vm.FocusMode;
        UpdateToolStates();
        AnimateWordsIn();
    }

    Flyout? _fieldMenu;
    readonly Dictionary<string, TextBlock> _fieldTicks = [];

    /// <summary>右键：挑卡片里显示哪些字段；左键仍是原来的“只看单词”开关。</summary>
    void OnFocusRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        ShowFieldMenu((FrameworkElement)sender);
    }

    /// <summary>
    /// 用普通 Flyout 而不是 MenuFlyout：MenuFlyout 一点条目就自己收起，
    /// 这里要的是只有点到弹层外面才关，好连着勾掉或勾回好几个字段。
    /// </summary>
    void ShowFieldMenu(FrameworkElement anchor)
    {
        if (_fieldMenu is null)
        {
            var rows = new StackPanel { Spacing = 1, MinWidth = 196, Margin = new Thickness(4) };
            var rowStyle = (Style)Application.Current.Resources["CardFieldRow"];
            var accent = ThemeBrush("Accent", "#FF0A7CFF");

            foreach (var (key, label) in CardField.Menu)
            {
                var tick = new TextBlock
                {
                    FontFamily = Icons.Font,
                    FontSize = 12,
                    Foreground = accent,
                    Text = Icons.Check,
                    Width = 15,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _fieldTicks[key] = tick;

                var row = new Button
                {
                    Style = rowStyle,
                    Tag = key,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            tick,
                            new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                        },
                    },
                };
                row.Click += OnCardFieldRowClick;
                rows.Children.Add(row);
            }

            _fieldMenu = new Flyout
            {
                Content = rows,
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,
                AllowFocusOnInteraction = false,
            };
        }

        // 勾常驻占位，隐藏时只把透明度归零，行宽不会跳。
        foreach (var (key, _) in CardField.Menu)
            _fieldTicks[key].Opacity = Vm.IsCardFieldShown(key) ? 1 : 0;

        _fieldMenu.ShowAt(anchor);
    }

    void OnCardFieldRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        var shown = !Vm.IsCardFieldShown(key);
        Vm.SetCardFieldShown(key, shown);
        _fieldTicks[key].Opacity = shown ? 1 : 0;
        AnimateWordsIn();
    }

    void OnToggleSearch(object sender, RoutedEventArgs e)
    {
        var show = !_searchOpen;
        if (!show) SearchBox.Text = "";
        AnimateSearchHost(show);
        UpdateToolStates();
    }

    /// <summary>开着的工具：底色淡蓝、图标换实心/斜杠版并上主题色；关着的回到样式默认色。</summary>
    void UpdateToolStates()
    {
        var accent = ThemeBrush("Accent", "#FF0A7CFF");
        FocusButton.Content = Vm.FocusMode ? Icons.EyeSlash : Icons.Eye;
        AdaptiveButton.Content = Vm.Adaptive ? Icons.AdaptiveOn : Icons.AdaptiveOff;

        SetToolState(FocusButton, Vm.FocusMode, accent);
        SetToolState(SearchButton, _searchOpen, accent);
        SetToolState(AdaptiveButton, Vm.Adaptive, accent);
        SetToolState(ScreenCardButton, _screenCard is not null, accent);
    }

    /// <summary>
    /// 关掉状态要用 ClearValue 撤掉本地值。给 Background / Foreground 直接赋 null 会把属性标成
    /// “显式的空”，ContentPresenter 继承到空画刷之后整个字形都不再绘制（图标直接消失，不是变灰）。
    /// </summary>
    void SetToolState(Button button, bool on, Brush accent)
    {
        if (on)
        {
            button.Background = _toolActive;
            button.Foreground = accent;
            return;
        }
        button.ClearValue(Control.BackgroundProperty);
        button.ClearValue(Control.ForegroundProperty);
    }

    void OnSearchChanged(object sender, TextChangedEventArgs e) => Vm.Search = SearchBox.Text;

    void OnSearchLostFocus(object sender, RoutedEventArgs e)
    {
        if (SearchBox.Text.Length == 0 && _searchOpen) OnToggleSearch(sender, e);
    }

    void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        SearchBox.Text = "";
        if (_searchOpen) OnToggleSearch(sender, e);
        e.Handled = true;
    }
}
