using System.Windows;
using SnipTool.Tray;
using Application = System.Windows.Application;

namespace SnipTool;

public partial class App : Application
{
    private TrayHost? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _tray = new TrayHost();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
