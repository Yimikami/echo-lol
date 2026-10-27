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
    internal static string Run(string executable, IEnumerable<string> arguments, int timeoutSeconds = 30, bool includeStandardError = false, Func<bool>? cancel = null)
    {
        var start = new ProcessStartInfo(Find(executable)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        process.PriorityClass = ProcessPriorityClass.BelowNormal;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        var elapsed = Stopwatch.StartNew();
        while (!process.WaitForExit(100))
        {
            if (cancel?.Invoke() == true) { process.Kill(true); process.WaitForExit(); throw new OperationCanceledException("Media preparation waits until gameplay ends."); }
            if (elapsed.Elapsed.TotalSeconds > timeoutSeconds) { process.Kill(true); process.WaitForExit(); throw new TimeoutException($"{executable} timed out."); }
        }
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
    public static (int Width, int Height) VideoSize(string path)
    {
        var text = Run("ffmpeg", ["-hide_banner", "-threads", "1", "-i", path, "-t", "0", "-f", "null", "-"], includeStandardError: true);
        var match = Regex.Match(text, @"Video:[^\r\n]*?\b(\d{2,5})x(\d{2,5})\b");
        if (!match.Success) throw new InvalidDataException("Video dimensions are unavailable.");
        return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
    }
    public static string Remux(string source, Func<bool>? cancel = null)
    {
        var output = Path.ChangeExtension(source, ".mp4");
        var temp = output + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            Run("ffmpeg", ["-hide_banner", "-v", "error", "-n", "-i", source, "-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-movflags", "+faststart", "-f", "mp4", temp], 120, cancel: cancel);
            _ = Duration(temp); File.Move(temp, output);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return output;
    }
    public static void Thumbnail(string source, string target, double at, int width = 480)
    {
        if (File.Exists(target)) return;
        Run("ffmpeg", ["-hide_banner", "-v", "error", "-n", "-threads", "1", "-ss", at.ToString("0.###", CultureInfo.InvariantCulture), "-i", source, "-frames:v", "1", "-filter_threads", "1", "-threads", "1", "-vf", $"scale={width}:-2", target]);
    }
    public static string[] VideoArguments(string? encoder) => encoder?.Contains("amf", StringComparison.OrdinalIgnoreCase) == true
        ? ["-c:v", "h264_amf", "-quality", "speed", "-rc", "cqp", "-qp_i", "20", "-qp_p", "20"]
        : encoder?.Contains("qsv", StringComparison.OrdinalIgnoreCase) == true
        ? ["-c:v", "h264_qsv", "-preset", "fast", "-global_quality", "20"]
        : ["-c:v", "h264_nvenc", "-preset", "p3", "-cq", "20"];
    public static string RemoveGaps(string source, string output, IReadOnlyList<Echo.Core.TimeRange> parts, string? encoder, Func<bool> cancel)
    {
        if (File.Exists(output)) { _ = Duration(output); return output; }
        var temporaryId = Guid.NewGuid().ToString("N");
        var partial = output + "." + temporaryId + ".partial"; var filterFile = output + "." + temporaryId + ".filter";
        var filters = new List<string>(); var inputs = new System.Text.StringBuilder();
        var seek = parts[0].Start;
        var readDuration = parts[^1].End - seek;
        for (int i = 0; i < parts.Count; i++)
        {
            var range = parts[i]; var from = (range.Start - seek).ToString("0.######", CultureInfo.InvariantCulture); var to = (range.End - seek).ToString("0.######", CultureInfo.InvariantCulture);
            filters.Add($"[0:v:0]trim=start={from}:end={to},setpts=PTS-STARTPTS[v{i}]");
            filters.Add($"[0:a:0]atrim=start={from}:end={to},asetpts=PTS-STARTPTS[a{i}]"); inputs.Append($"[v{i}][a{i}]");
        }
        filters.Add($"{inputs}concat=n={parts.Count}:v=1:a=1[v][a]");
        try
        {
            File.WriteAllText(filterFile, string.Join(';', filters));
            Run("ffmpeg", new[] { "-hide_banner", "-v", "error", "-n", "-threads", "1", "-ss", seek.ToString("0.######", CultureInfo.InvariantCulture), "-t", readDuration.ToString("0.######", CultureInfo.InvariantCulture), "-i", source, "-filter_complex_threads", "1", "-filter_complex_script", filterFile, "-map", "[v]", "-map", "[a]" }
                .Concat(VideoArguments(encoder)).Concat(["-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-f", "mp4", partial]), 180, cancel: cancel);
            var duration = Duration(partial);
            if (Math.Abs(duration - parts.Sum(p => p.Duration)) > .25) throw new InvalidDataException("Prepared clip duration did not match the visible footage.");
            File.Move(partial, output); return output;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); if (File.Exists(filterFile)) File.Delete(filterFile); }
    }
}
