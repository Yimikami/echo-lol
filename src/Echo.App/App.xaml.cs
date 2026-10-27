using System.Windows;
using Echo.Core;

namespace Echo.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var index = Array.IndexOf(e.Args, "--data-dir");
        var paths = new HostPaths(index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null);
        var window = new MainWindow(paths, !e.Args.Contains("--no-recorder")); MainWindow = window; window.Show();
    }
}
