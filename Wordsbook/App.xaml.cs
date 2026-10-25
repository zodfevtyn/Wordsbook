using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Wordsbook.Services;

namespace Wordsbook;

public partial class App : Application
{
    public static Window? Main { get; private set; }
    public static Store Store { get; private set; } = null!;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Store = new Store(DispatcherQueue.GetForCurrentThread());
        Store.Load();

        // StaticResource 在窗口 InitializeComponent 时解析，所以字体必须在建窗口之前换好。
        var (text, display, round) = Store.Data.Settings.Font == "system"
            ? ("Segoe UI Variable Text", "Segoe UI Variable Display", "Segoe UI Variable Display")
            : ("SF Pro Text", "SF Pro Display", "SF Pro Rounded");

        Resources["AppTextFont"] = new FontFamily(text);
        Resources["AppDisplayFont"] = new FontFamily(display);
        Resources["AppRoundFont"] = new FontFamily(round);

        Main = new MainWindow(Store);
        Main.Activate();
    }
}
