using System.Text.Json;

namespace Echo.Core;

public sealed record InputSample(double Time, string Control, bool Down);
public static class GameplayInputs
{
    public static readonly HashSet<string> Allowed = OverlayAppearance.KeyboardKeys.Concat(["MouseLeft","MouseRight","MouseMiddle","Mouse4","Mouse5","WheelUp","WheelDown"]).ToHashSet(StringComparer.Ordinal);
    public static List<InputSample> Read(string? path)
    {
        var result = new List<InputSample>();
        if (path == null || !File.Exists(path)) return result;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var reader = new StreamReader(file);
        while (reader.ReadLine() is { } line)
        {
            try { var sample = JsonSerializer.Deserialize<InputSample>(line, JsonStore.Options); if (sample != null && double.IsFinite(sample.Time) && sample.Time >= 0 && Allowed.Contains(sample.Control)) result.Add(sample); }
            catch (JsonException) { /* An interrupted final line does not invalidate earlier samples. */ }
        }
        return result.OrderBy(s => s.Time).ToList();
    }
    public static HashSet<string> Held(IReadOnlyList<InputSample> samples, double time)
    {
        var held = new HashSet<string>();
        foreach (var s in samples) { if (s.Time > time) break; if (s.Down) held.Add(s.Control); else held.Remove(s.Control); }
        return held;
    }
    // Observed keyboard/button presses in the trailing 60 seconds; motion and auto-repeat do not count.
    public static int Apm(IReadOnlyList<InputSample> samples, double time) => samples.Count(s => s.Down && s.Time > time - 60 && s.Time <= time);
    public static double RecordingTime(MediaClip clip, double fileTime)
    {
        if (clip.Segments.Count == 0) return clip.RecordingOrigin + fileTime;
        foreach (var segment in clip.Segments)
            if (fileTime >= segment.FileStart && fileTime < segment.FileStart + segment.Duration) return segment.RecordingStart + fileTime - segment.FileStart;
        return clip.Segments[^1].RecordingStart + clip.Segments[^1].Duration;
    }
}

