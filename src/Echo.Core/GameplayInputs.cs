using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Echo.Core;

public sealed record InputSample(double Time, string Control, bool Down);
public sealed record InputKey(string Id, string Label, int X, int Y, int Width = 32, int Height = 30);
public static class GameplayInputs
{
    public static readonly InputKey[] Keys = [
        new("1","1",12,42),new("2","2",50,42),new("3","3",88,42),new("4","4",126,42),new("5","5",164,42),new("6","6",202,42),new("7","7",240,42),
        new("Q","Q",12,80),new("W","W",50,80),new("E","E",88,80),new("R","R",126,80),new("D","D",188,80),new("F","F",226,80),
        new("Ctrl","CTRL",12,118,52),new("Shift","SHIFT",70,118,58),new("Space","SPACE",134,118,94),new("Alt","ALT",234,118,38),
        new("MouseLeft","L",292,42,32),new("MouseRight","R",330,42,32),new("MouseMiddle","M",292,80,70),new("Mouse4","M4",292,118,32),new("Mouse5","M5",330,118,32),
        new("A","A",12,156),new("S","S",50,156),new("B","B",88,156),new("X","X",126,156),new("C","C",164,156),new("G","G",202,156),new("H","H",240,156),new("WheelUp","+",292,156),new("WheelDown","−",330,156),
        new("P","P",12,194),new("T","T",50,194),new("V","V",88,194),new("Y","Y",126,194),new("Tab","TAB",164,194,108)
    ];
    public static readonly HashSet<string> Allowed = Keys.Select(k=>k.Id).Concat("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Select(c=>c.ToString())).Concat(Enumerable.Range(1,12).Select(n=>"F"+n)).Concat(new[]{"Backspace","Backtick","Caps","Left","Right","Up","Down"}).ToHashSet(StringComparer.Ordinal);
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

