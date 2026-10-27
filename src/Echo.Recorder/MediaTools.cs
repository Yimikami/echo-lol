using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Echo.Recorder;

internal static class MediaTools
{
    internal static string Find(string name)
    {
        foreach (var path in new[] { Path.Combine(AppContext.BaseDirectory, "tools", name + ".exe"), Path.Combine(AppContext.BaseDirectory, "runtime", "tools", name + ".exe") })
            if (File.Exists(path)) return path;
        return name; // PATH, developer setup
    }
    internal static string Run(string executable, IEnumerable<string> arguments, int timeoutSeconds = 30)
    {
        var start = new ProcessStartInfo(Find(executable)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutSeconds * 1000)) { process.Kill(true); throw new TimeoutException($"{executable} timed out."); }
        if (process.ExitCode != 0) throw new InvalidOperationException($"{executable}: {stderr.GetAwaiter().GetResult()[..Math.Min(1000, stderr.Result.Length)]}");
        return stdout.GetAwaiter().GetResult();
    }
    public static double Duration(string path)
    {
        var text = Run("ffprobe", ["-v", "error", "-show_entries", "format=duration", "-of", "json", path]);
        using var json = JsonDocument.Parse(text);
        var duration = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException("The saved recording has no valid duration.");
        return duration;
    }
    public static string Remux(string source)
    {
        var output = Path.ChangeExtension(source, ".mp4");
        var temp = output + ".partial";
        Run("ffmpeg", ["-hide_banner", "-v", "error", "-n", "-i", source, "-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-movflags", "+faststart", "-f", "mp4", temp], 120);
        _ = Duration(temp);
        File.Move(temp, output); // Never overwrite a user file.
        return output;
    }
    public static void Thumbnail(string source, string target, double at)
    {
        if (File.Exists(target)) return;
        Run("ffmpeg", ["-hide_banner", "-v", "error", "-n", "-ss", at.ToString("0.###", CultureInfo.InvariantCulture), "-i", source, "-frames:v", "1", "-vf", "scale=480:-2", target]);
    }
}
