using Microsoft.UI.Xaml;

namespace OWLProtect.WindowsClientUi;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var options = AppLaunchOptions.Parse(Environment.GetCommandLineArgs().Skip(1));
        _window = new MainWindow(options);
        _window.Activate();
    }
}
