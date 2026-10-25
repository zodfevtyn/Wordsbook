using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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

    public AppViewModel Vm { get; }

    public MainWindow(Store store)
    {
        _store = store;
        Vm = new AppViewModel(store, DispatcherQueue.GetForCurrentThread());

        InitializeComponent();

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
        // 弹窗高度跟窗口走，窗口矮的时候让内容滚动而不是把底部圆角顶出屏幕。
        EditorScrim.SizeChanged += (_, e) => Editor.MaxHeight = Math.Max(240, e.NewSize.Height - 48);
        Editor.Saved += entry =>
        {
            Vm.Save(entry);
            CloseEditor();
        };
        Settings.Changed += ApplySettings;
        Settings.CloseRequested += () => SettingsScrim.Visibility = Visibility.Collapsed;

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
        }
    }

    void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (_zoomReady) return;
        _zoomReady = true;
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
        SettingsScrim.Visibility = Visibility.Visible;
    }

    void OnSettingsScrimTapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource)) SettingsScrim.Visibility = Visibility.Collapsed;
    }

    void OnStartToday(object sender, RoutedEventArgs e) => Vm.StartToday();

    async void OnAddCategory(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedLangId is not { } langId) return;

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
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && input.Text.Trim().Length > 0)
            Vm.AddCategory(langId, input.Text.Trim());
    }

    void OnEditLanguages(object sender, RoutedEventArgs e)
    {
        var node = Vm.LangNodes.FirstOrDefault(n => n.Id == Vm.SelectedLangId) ?? Vm.LangNodes.FirstOrDefault();
        var anchor = (FrameworkElement)sender;
        ShowLanguageMenu(node, anchor, new Windows.Foundation.Point(0, anchor.ActualHeight));
    }

    void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) Vm.SelectLanguage(id);
    }

    void OnLanguageChevron(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) Vm.ToggleExpanded(id);
    }

    void OnMonthClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key }) Vm.SelectMonth(key);
    }

    void OnMonthChevron(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key }) Vm.ToggleExpanded(key);
    }

    void OnDayClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key }) Vm.SelectDay(key);
    }

    void OnCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) Vm.SelectCategory(id);
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
        if (sender is Border b) b.Opacity = 1;
    }

    void OnAddTileExit(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border b) b.Opacity = 0.55;
    }

    void ShowEditor(bool visible) => EditorScrim.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    void CloseEditor() => ShowEditor(false);

    void OnScrimTapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource)) CloseEditor();
    }

    void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;

        if (SettingsScrim.Visibility == Visibility.Visible) SettingsScrim.Visibility = Visibility.Collapsed;
        else if (EditorScrim.Visibility == Visibility.Visible) CloseEditor();
        else return;

        e.Handled = true;
    }

    // ------------------------------------------------------------ 工具栏

    void OnToggleLayout(object sender, RoutedEventArgs e)
    {
        Vm.GridView = !Vm.GridView;
        ApplyLayout();
        UpdateLayoutButton();
    }

    void UpdateLayoutButton() => LayoutButton.Content = Vm.GridView ? Icons.List : Icons.Grid;

    void ApplyLayout()
    {
        if (Vm.GridView)
        {
            WordList.ItemTemplate = (DataTemplate)RootGrid.Resources["GridCardTemplate"];
            WordList.ItemsPanel = (ItemsPanelTemplate)RootGrid.Resources["WrapPanelTemplate"];
        }
        else
        {
            WordList.ItemTemplate = (DataTemplate)RootGrid.Resources["ListRowTemplate"];
            WordList.ItemsPanel = (ItemsPanelTemplate)RootGrid.Resources["StackPanelTemplate"];
        }
    }

    void OnToggleFocus(object sender, RoutedEventArgs e)
    {
        Vm.FocusMode = !Vm.FocusMode;
        UpdateToolStates();
    }

    void OnToggleSearch(object sender, RoutedEventArgs e)
    {
        var show = SearchHost.Visibility != Visibility.Visible;
        SearchHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) SearchBox.Text = "";
        UpdateToolStates();
        if (show) SearchBox.Focus(FocusState.Programmatic);
    }

    void UpdateToolStates()
    {
        FocusButton.Background = Vm.FocusMode ? _toolActive : null;
        SearchButton.Background = SearchHost.Visibility == Visibility.Visible ? _toolActive : null;
        LayoutButton.Background = null;
    }

    void OnSearchChanged(object sender, TextChangedEventArgs e) => Vm.Search = SearchBox.Text;

    void OnSearchLostFocus(object sender, RoutedEventArgs e)
    {
        if (SearchBox.Text.Length == 0 && SearchHost.Visibility == Visibility.Visible) OnToggleSearch(sender, e);
    }

    void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        SearchBox.Text = "";
        OnToggleSearch(sender, e);
        e.Handled = true;
    }
}
