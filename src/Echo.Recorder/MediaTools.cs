using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Echo.Recorder;

internal static class MediaTools
{
    internal static string Find(string name)
    {
        foreach (var path in new[] { Path.Combine(AppContext.BaseDirectory, "tools", name + ".exe"), Path.Combine(AppContext.BaseDirectory, "runtime", "tools", name + ".exe") })
            if (File.Exists(path)) return path;
        return name; // PATH, developer setup
    }
    internal static string Run(string executable, IEnumerable<string> arguments, int timeoutSeconds = 30, bool includeStandardError = false)
    {
        var start = new ProcessStartInfo(Find(executable)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutSeconds * 1000)) { process.Kill(true); throw new TimeoutException($"{executable} timed out."); }
        if (process.ExitCode != 0) throw new InvalidOperationException($"{executable}: {stderr.GetAwaiter().GetResult()[..Math.Min(1000, stderr.Result.Length)]}");
        return stdout.GetAwaiter().GetResult() + (includeStandardError ? stderr.GetAwaiter().GetResult() : "");
    }
    public static double Duration(string path)
    {
        // Reuse FFmpeg's demuxer instead of shipping a second ~100 MB executable.
        // A zero-length null output inspects the container without decoding a match.
        var text = Run("ffmpeg", ["-hide_banner", "-threads", "1", "-i", path, "-t", "0", "-f", "null", "-"], includeStandardError: true);
        var match = Regex.Match(text, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.CultureInvariant);
        if (!match.Success) throw new InvalidDataException("The recording has no readable container duration.");
        var duration = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
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
    public static void Thumbnail(string source, string target, double at, int width = 480)
    {
        if (File.Exists(target)) return;
        Run("ffmpeg", ["-hide_banner", "-v", "error", "-n", "-threads", "1", "-ss", at.ToString("0.###", CultureInfo.InvariantCulture), "-i", source, "-frames:v", "1", "-filter_threads", "1", "-threads", "1", "-vf", $"scale={width}:-2", target]);
    }
}
