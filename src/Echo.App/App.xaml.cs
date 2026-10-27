using System.Windows;
using Echo.Core;

namespace Echo.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
        var index = Array.IndexOf(e.Args, "--data-dir");
        var paths = new HostPaths(index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null);
        if (e.Args.Contains("--verify-ui") || e.Args.Contains("--verify-behavior"))
        {
            // Render and exercise our own visual tree without opening or controlling any desktop window.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var outputIndex = Array.IndexOf(e.Args, "--output");
            var output = outputIndex >= 0 && outputIndex + 1 < e.Args.Length ? e.Args[outputIndex + 1] : System.IO.Path.Combine(paths.Root, "ui-check");
            System.IO.Directory.CreateDirectory(output);
            try { var preview = new MainWindow(paths, false); await preview.VerifyUi(output, !e.Args.Contains("--verify-behavior")); preview.Close(); Shutdown(0); }
            catch (Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(output, "error.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        var window = new MainWindow(paths, !e.Args.Contains("--no-recorder")); MainWindow = window; window.Show();
    }
}
