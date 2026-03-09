using System.Diagnostics;
using System.Windows.Forms;

namespace OWLProtect.WindowsClientTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly string _uiExePath;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private Icon? _currentIcon;

    public TrayApplicationContext()
    {
        _uiExePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "ui", "OWLProtect.WindowsClientUi.exe"));
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open OWLProtect Client", null, (_, _) => OpenUiWindow());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                OpenUiWindow();
            }
        };

        _pollTimer = new System.Windows.Forms.Timer
        {
            Interval = 5000
        };
        _pollTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _pollTimer.Start();

        UpdateStatus(StatusPresentation.CreateUnavailableStatus());
        _ = RefreshStatusAsync();
    }

    protected override void ExitThreadCore()
    {
        _pollTimer.Stop();
        _notifyIcon.Visible = false;
        _currentIcon?.Dispose();
        _notifyIcon.Dispose();
        base.ExitThreadCore();
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var response = await PipeClient.RequestStatusAsync();
            UpdateStatus(response.Status);
        }
        catch
        {
            UpdateStatus(StatusPresentation.CreateUnavailableStatus());
        }
    }

    private void UpdateStatus(ClientStatus status)
    {
        _currentIcon?.Dispose();
        _currentIcon = TrayIconRenderer.Create(status);
        _notifyIcon.Icon = _currentIcon;
        _notifyIcon.Text = StatusPresentation.ToTooltip(status);
    }

    private void OpenUiWindow()
    {
        if (!File.Exists(_uiExePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _uiExePath,
            Arguments = "--show-window",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(_uiExePath) ?? Environment.CurrentDirectory
        });
    }
}
