using System.Windows;
using Echo.Core;

namespace Echo.App;

public partial class App : Application
{
    public static bool IsExiting { get; private set; }
    internal static bool IsShutdownProbe { get; private set; }
    private System.Windows.Threading.DispatcherTimer? _exitMonitor;
    private ApplicationActivation? _activation;
    private Window? _startupDialog;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        IsShutdownProbe = e.Args.Contains("--shutdown-probe");
        bool verifying = IsShutdownProbe || e.Args.Any(arg => arg is "--verify-overlay" or "--verify-ui" or "--verify-behavior");
        if (!verifying)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _activation = ApplicationActivation.TryAcquire();
            if (_activation == null)
            {
                if (await ApplicationActivation.TryActivateAsync(2000) || (_activation = ApplicationActivation.TryAcquire()) == null)
                {
                    Shutdown();
                    return;
                }
            }
            _ = _activation.ListenAsync(() => Dispatcher.BeginInvoke(ActivateWindow));
        }

        var index = Array.IndexOf(e.Args, "--data-dir");
        var paths = new HostPaths(index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null);
        var firstRun = !System.IO.File.Exists(paths.Settings);
        try { ThemeService.Apply(paths.LoadSettings().Theme); } catch { ThemeService.Apply("Dark"); }
        if(e.Args.Contains("--verify-overlay"))
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{await OverlayVerification.Run(paths);Shutdown(0);}catch(Exception ex){System.IO.File.WriteAllText(System.IO.Path.Combine(paths.Root,"overlay-error.txt"),ex.ToString());Shutdown(1);}return;
        }
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
        var lastExit = ApplicationExit.Current(paths);
        _exitMonitor = new() { Interval = TimeSpan.FromMilliseconds(250) };
        _exitMonitor.Tick += async (_, _) =>
        {
            if (IsExiting || ApplicationExit.Current(paths) is not { } request || request == lastExit) return;
            IsExiting = true; _exitMonitor.Stop();
            await System.Threading.Tasks.Task.Yield();
            Shutdown();
        };
        _exitMonitor.Start();
        if (firstRun && !IsShutdownProbe && !e.Args.Contains("--no-onboarding"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _startupDialog = new OnboardingWindow(paths);
            _startupDialog.ShowDialog();
            _startupDialog = null;
        }
        var window = new MainWindow(paths, !e.Args.Contains("--no-recorder")); MainWindow = window;
        if (e.Args.Contains("--shutdown-probe")) { ShutdownMode = ShutdownMode.OnExplicitShutdown; await window.StartShutdownProbe(); System.IO.File.WriteAllText(System.IO.Path.Combine(paths.Root,"shutdown-ready"), "ready"); }
        else {ShutdownMode=ShutdownMode.OnLastWindowClose;window.Show();}
    }

    private void ActivateWindow()
    {
        if (IsExiting) return;
        var window = _startupDialog ?? MainWindow;
        if (window == null) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        var dialog = window.OwnedWindows.OfType<Window>().LastOrDefault(w => w.IsVisible);
        (dialog ?? window).Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activation?.Dispose();
        base.OnExit(e);
    }

}
