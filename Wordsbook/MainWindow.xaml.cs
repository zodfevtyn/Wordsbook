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

        ApplyTheme();
        UpdateLayoutButton();
        UpdateToolStates();
        Vm.Initialize();
        Vm.SetSidebarOpen(Vm.IsSidebarOpen, animate: false);
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
    }

    void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange) ApplyResizeRegions();
        var maxed = sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        TrafficMax.Content = maxed ? Icons.ChromeRestore : Icons.ChromeMaximize;
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

    void ShowLanguageMenu(LangNode? node, FrameworkElement anchor, Windows.Foundation.Point position)
    {
        var menu = new MenuFlyout();

        var edit = new MenuFlyoutItem { Text = "Rename / change flag", Icon = new FontIcon { Glyph = Icons.Edit }, IsEnabled = node is not null };
        edit.Click += async (_, _) => await EditLanguageAsync(node);

        var add = new MenuFlyoutItem { Text = "New language", Icon = new FontIcon { Glyph = Icons.Add } };
        add.Click += async (_, _) => await EditLanguageAsync(null);

        var remove = new MenuFlyoutItem { Text = "Delete language", Icon = new FontIcon { Glyph = Icons.Delete }, IsEnabled = node is not null };
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

        var edit = new MenuFlyoutItem { Text = "Edit", Icon = new FontIcon { Glyph = Icons.Edit } };
        edit.Click += (_, _) =>
        {
            if (view.Model is not null)
            {
                Editor.OpenEdit(Vm, view.Model);
                ShowEditor(true);
            }
        };

        var del = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = Icons.Delete } };
        del.Click += (_, _) =>
        {
            if (view.Model is { } model) Vm.Delete(model);
        };

        menu.Items.Add(edit);
        menu.Items.Add(del);
        menu.ShowAt((FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
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

    void OnToggleSearch(object sender, RoutedEventArgs e)
    {
        var show = !_searchOpen;
        if (!show) SearchBox.Text = "";
        AnimateSearchHost(show);
        UpdateToolStates();
    }

    void UpdateToolStates()
    {
        FocusButton.Background = Vm.FocusMode ? _toolActive : null;
        SearchButton.Background = _searchOpen ? _toolActive : null;
        AdaptiveButton.Background = Vm.Adaptive ? _toolActive : null;
        LayoutButton.Background = null;
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
