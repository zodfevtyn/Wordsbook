using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wordsbook.ViewModels;

namespace Wordsbook.Controls;

/// <summary>设置浮层：外观主题、界面缩放、字体。改动通过 Changed 通知主窗口落地。</summary>
public sealed partial class SettingsPanel : UserControl
{
    AppViewModel? _vm;
    bool _syncing;

    public event Action? Changed;
    public event Action? CloseRequested;

    public SettingsPanel() => InitializeComponent();

    public void SyncFrom(AppViewModel vm)
    {
        _syncing = true;
        _vm = vm;

        (vm.Theme switch { "light" => ThemeLight, "dark" => ThemeDark, _ => ThemeSystem }).IsChecked = true;
        (vm.Scale switch { 1.5 => Scale15, 1.25 => Scale125, _ => Scale1 }).IsChecked = true;
        (vm.Font == "system" ? FontSystem : FontSf).IsChecked = true;

        _syncing = false;
    }

    void OnThemeChanged(object sender, RoutedEventArgs e) =>
        Commit(vm => vm.Theme = Sender(sender) == ThemeLight ? "light" : Sender(sender) == ThemeDark ? "dark" : "system");

    void OnScaleChanged(object sender, RoutedEventArgs e) =>
        Commit(vm => vm.Scale = Sender(sender) == Scale15 ? 1.5 : Sender(sender) == Scale125 ? 1.25 : 1d);

    void OnFontChanged(object sender, RoutedEventArgs e) =>
        Commit(vm => vm.Font = Sender(sender) == FontSystem ? "system" : "sf");

    static RadioButton Sender(object sender) => (RadioButton)sender;

    void Commit(Action<AppViewModel> apply)
    {
        if (_syncing || _vm is null) return;
        apply(_vm);
        Changed?.Invoke();
    }

    void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
