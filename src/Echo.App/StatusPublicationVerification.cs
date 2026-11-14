using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Echo.Core;

namespace Echo.App;

public partial class MainWindow
{
    private async Task VerifyStatusPublication(string output, Action<string, bool> check)
    {
        var paths = new HostPaths(Path.Combine(output, "status-publication-profile"));
        JsonStore.Write(paths.Settings, new EchoSettings { OutputDirectory = Path.Combine(paths.Root, "recordings") });
        JsonStore.Write(paths.Status, new RecorderStatus { State = "Old snapshot", UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        var type = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Echo.Recorder.dll")).GetType("Echo.Recorder.RecorderHost")!;
        using var cancellation = new CancellationTokenSource();
        var host = Activator.CreateInstance(type, paths, cancellation.Token, true)!;
        var publish = type.GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var state = type.GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var service = (Task)type.GetMethod("ServeAsync")!.Invoke(host, null)!;
        var probe = new MainWindow(paths, false);
        probe._timer.Stop();
        var savedError = Console.Error;
        using var diagnostic = new StringWriter();
        try
        {
            Console.SetError(diagnostic);
            var clock = Stopwatch.StartNew();
            using (var locked = new FileStream(paths.Status, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                for (int i = 1; i <= 5; i++)
                    publish.Invoke(host, [new RecorderStatus { Armed = true, Recording = true, State = "Recording", Frames = i * 60, ElapsedSeconds = i }]);
                clock.Stop();
                check("status replacement failure does not interrupt active recording", state.GetValue(host) is RecorderStatus { Armed: true, Recording: true, Frames: 300, Error: null });
                check("failed status writes keep the last valid snapshot and do not leave temporary files", JsonStore.Read<RecorderStatus>(paths.Status)!.State == "Old snapshot" && Directory.GetFiles(paths.Root, "*.tmp").Length == 0);
                check("a sustained status lock logs once rather than every heartbeat", diagnostic.ToString().Split("status update skipped", StringSplitOptions.None).Length == 2);
                File.WriteAllText(Path.Combine(output, "status-publication-timing.txt"), $"Five locked status publications: {clock.Elapsed.TotalMilliseconds:F1} ms (includes process metrics). No retry backoff in this path.");

                await probe.PollRecorderStatus();
                check("the live command pipe reports recording while the status file is locked", probe._status is { Recording: true, Frames: 300 } && probe.RecorderLabel.Text == "REC");
                probe.RefreshStatus();
                check("a stale status file cannot replace a newer live recording status", probe.CaptureActive() && probe._status.Recording && probe.RecorderLabel.Text == "REC" && !probe.ExportButton.IsEnabled);
            }
            using (var locked = new FileStream(paths.Status, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                probe.RefreshStatus();
                check("an unreadable status file keeps the latest live status and export guard", probe.CaptureActive() && probe.RecorderLabel.Text == "REC" && !probe.ExportButton.IsEnabled);
                var restarting = Activator.CreateInstance(type, paths, cancellation.Token, true)!;
                check("an unreadable old status snapshot does not prevent recorder startup", state.GetValue(restarting) is RecorderStatus { Recording: false, UpdatedAt: var updated } && updated == DateTimeOffset.MinValue);
                ((Process)type.GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(restarting)!).Dispose();
            }
            publish.Invoke(host, [new RecorderStatus { Armed = true, Recording = true, State = "Recording", Frames = 360 }]);
            check("the next heartbeat restores status publication when the lock is released", JsonStore.Read<RecorderStatus>(paths.Status) is { Recording: true, Frames: 360 } && diagnostic.ToString().Contains("writable again"));
            // A real encoder/capture error must still reach the UI after recovery.
            publish.Invoke(host, [new RecorderStatus { State = "Recording needs attention", Error = "Encoder failed" }]);
            probe.RefreshStatus();
            check("real capture failures remain visible after status-file recovery", probe._status.Error == "Encoder failed" && probe.Toast.Visibility == Visibility.Visible && !probe.CaptureActive());
        }
        finally
        {
            Console.SetError(savedError);
            cancellation.Cancel();
            try { await service; } catch (OperationCanceledException) { }
            probe.Close();
            ((Process)type.GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!).Dispose();
        }
    }
}
