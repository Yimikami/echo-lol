using System.Diagnostics;
using Echo.Core;

namespace Echo.Recorder;

/// <summary>Native plugins get one capture lifecycle per process; a native fault cannot end automatic detection permanently.</summary>
internal static class Supervisor
{
    private sealed record Handoff(bool Armed, string? Error);
    public static int Run(HostPaths paths)
    {
        using var singleton = new Mutex(true, "Local\\" + paths.PipeName + "-supervisor", out var first);
        if (!first) return 0;
        if (File.Exists(paths.StopRequest)) File.Delete(paths.StopRequest);
        bool armed = true;
        int consecutiveFaults = 0;
        while (!File.Exists(paths.StopRequest))
        {
            if (File.Exists(paths.Handoff)) File.Delete(paths.Handoff);
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("--worker"); info.ArgumentList.Add("--data-dir"); info.ArgumentList.Add(paths.Root);
            if (!armed) info.ArgumentList.Add("--paused");
            using var child = Process.Start(info) ?? throw new InvalidOperationException("Could not start the recording worker.");
            child.WaitForExit();
            if (File.Exists(paths.StopRequest)) break;
            var handoff = JsonStore.Read<Handoff>(paths.Handoff);
            if (child.ExitCode == 0 && handoff != null)
            {
                armed = handoff.Armed; consecutiveFaults = 0;
            }
            else
            {
                consecutiveFaults++;
                RecoverInterruptedSession(paths);
                armed = false; // Do not repeatedly restart native capture against the same failing game.
                var status = JsonStore.Read<RecorderStatus>(paths.Status) ?? new();
                JsonStore.Write(paths.Status, status with { Armed = false, Recording = false, State = "Capture paused", Error = "The native recording worker stopped. Saved files were retained. Resume capture to retry.", UpdatedAt = DateTimeOffset.UtcNow });
                if (consecutiveFaults >= 3) return 1;
            }
            Thread.Sleep(500);
        }
        return 0;
    }
    private static void RecoverInterruptedSession(HostPaths paths)
    {
        try
        {
            var status = JsonStore.Read<RecorderStatus>(paths.Status);
            if (status?.SessionId == null) return;
            var settings = paths.LoadSettings();
            var folder = Path.Combine(settings.OutputDirectory, status.SessionId);
            var file = Path.Combine(folder, "session.json");
            var session = JsonStore.Read<Session>(file);
            if (session == null || session.EndedAt != null) return;
            session.State = "Interrupted"; session.EndedAt = DateTimeOffset.UtcNow; session.Duration = status.ElapsedSeconds;
            session.Error = "Native worker ended unexpectedly; recorded files were retained.";
            foreach (var clip in session.Clips.ToArray())
            {
                if (!clip.Path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) || !File.Exists(clip.Path)) continue;
                try { var mp4 = MediaTools.Remux(clip.Path); session.Clips[session.Clips.IndexOf(clip)] = clip with { Path = mp4 }; }
                catch (Exception) { /* Preserve the original and the catalog entry for manual recovery. */ }
            }
            var full = session.RecordingPath ?? Path.Combine(folder, "match.mkv");
            if (session.Mode == RecordingMode.FullMatch && File.Exists(full))
            {
                try { session.RecordingPath = full.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ? MediaTools.Remux(full) : full; session.Duration = MediaTools.Duration(session.RecordingPath); }
                catch (Exception) { session.RecordingPath = full; }
            }
            JsonStore.Write(file, session);
        }
        catch (Exception) { /* Recovery must not remove or overwrite any original media. */ }
    }
}
