using AssetLibrary.Windows.Session;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace AssetLibrary.Windows.Settings;

public partial class App : Application
{
    public App() { InitializeComponent(); }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var instance = AppInstance.FindOrRegisterForKey("AssetLibrary.Settings." + LocalPipe.UserSession);
        if (!instance.IsCurrent)
        {
            await instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            Exit();
            return;
        }
        var window = new MainWindow();
        instance.Activated += (_, _) => window.DispatcherQueue.TryEnqueue(() => window.Activate());
        window.Activate();
    }
}
