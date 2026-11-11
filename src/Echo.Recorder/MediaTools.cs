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
    internal static string Run(string executable, IEnumerable<string> arguments, int timeoutSeconds = 30, bool includeStandardError = false,
        Func<bool>? cancel = null, Action<double>? progress = null)
    {
        var start = new ProcessStartInfo(Find(executable)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (progress != null)
            foreach (var arg in new[] { "-nostats", "-stats_period", "0.25", "-progress", "pipe:1" }) start.ArgumentList.Add(arg);
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        process.PriorityClass = ProcessPriorityClass.BelowNormal;
        var stdout = progress == null ? process.StandardOutput.ReadToEndAsync() : ReadProgressAsync(process.StandardOutput, progress);
        var stderr = process.StandardError.ReadToEndAsync();
        var elapsed = Stopwatch.StartNew();
        try
        {
            while (!process.WaitForExit(100))
            {
                if (cancel?.Invoke() == true) { process.Kill(true); process.WaitForExit(); throw new OperationCanceledException("Media preparation waits until gameplay ends."); }
                if (elapsed.Elapsed.TotalSeconds > timeoutSeconds) { process.Kill(true); process.WaitForExit(); throw new TimeoutException($"{executable} timed out."); }
            }
        }
        finally
        {
            if (progress != null && process.HasExited) stdout.GetAwaiter().GetResult();
        }
        if (process.ExitCode != 0) throw new InvalidOperationException($"{executable}: {stderr.GetAwaiter().GetResult()[..Math.Min(1000, stderr.Result.Length)]}");
        return stdout.GetAwaiter().GetResult() + (includeStandardError ? stderr.GetAwaiter().GetResult() : "");
    }

    private static async Task<string> ReadProgressAsync(StreamReader output, Action<double> report)
    {
        const string key = "out_time_us=";
        while (await output.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith(key, StringComparison.Ordinal)
                && long.TryParse(line.AsSpan(key.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out long timestamp)
                && timestamp >= 0)
                report(timestamp / 1_000_000d);
        }
        return "";
    }

    private static string Inspect(string path) =>
        Run("ffmpeg", ["-hide_banner", "-threads", "1", "-i", path, "-t", "0", "-f", "null", "-"], includeStandardError: true);

    public static int AudioStreamCount(string path) =>
        Regex.Matches(Inspect(path).Split("Stream mapping:", 2)[0], @"^\s*Stream #0:\d+[^\r\n]*?: Audio:", RegexOptions.Multiline).Count;

    public static double Duration(string path)
    {
        // Reuse FFmpeg's demuxer instead of shipping a second ~100 MB executable.
        // A zero-length null output inspects the container without decoding a match.
        var text = Inspect(path);
        var match = Regex.Match(text, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.CultureInvariant);
        if (!match.Success) throw new InvalidDataException("The recording has no readable container duration.");
        var duration = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException("The saved recording has no valid duration.");
        return duration;
    }
    public static (int Width, int Height) VideoSize(string path)
    {
        var text = Inspect(path);
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
    public static void Thumbnail(string source, string target, double at, int width = 480, Func<bool>? cancel = null)
    {
        if (File.Exists(target)) return;
        Run("ffmpeg", ["-hide_banner", "-v", "error", "-n", "-threads", "1", "-ss", at.ToString("0.###", CultureInfo.InvariantCulture), "-i", source, "-frames:v", "1", "-filter_threads", "1", "-threads", "1", "-vf", $"scale={width}:-2", target], cancel: cancel);
    }
    public static string[] VideoArguments(string? encoder) => encoder == "software-export"
        ? ["-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-threads", "2"]
        : encoder?.Contains("amf", StringComparison.OrdinalIgnoreCase) == true
        ? ["-c:v", "h264_amf", "-quality", "speed", "-rc", "cqp", "-qp_i", "20", "-qp_p", "20"]
        : encoder?.Contains("qsv", StringComparison.OrdinalIgnoreCase) == true
        ? ["-c:v", "h264_qsv", "-preset", "fast", "-global_quality", "20"]
        : ["-c:v", "h264_nvenc", "-preset", "p3", "-cq", "20"];
    internal static void JoinReplays(IReadOnlyList<Echo.Core.ReplayPart> parts, string output, string? encoder, Func<bool> cancel)
    {
        var expected = parts.Sum(p => p.Duration);
        if (File.Exists(output))
        {
            if (Math.Abs(Duration(output) - expected) > .25) throw new InvalidDataException("The assembled replay has an unexpected duration.");
            return;
        }
        var work = output + "." + Guid.NewGuid().ToString("N") + ".work";
        var partial = output + "." + Guid.NewGuid().ToString("N") + ".partial";
        Directory.CreateDirectory(work);
        try
        {
            var streams = AudioStreamCount(parts[0].Path);
            var files = new List<string>();
            for (var i = 0; i < parts.Count; i++)
            {
                if (cancel()) throw new OperationCanceledException();
                var part = parts[i];
                if (AudioStreamCount(part.Path) != streams) throw new InvalidDataException("Replay audio tracks differ.");
                var target = Path.Combine(work, i + ".mp4");
                var args = new List<string> { "-hide_banner", "-v", "error", "-n", "-threads", "1",
                    "-ss", part.Start.ToString("0.######", CultureInfo.InvariantCulture), "-i", part.Path,
                    "-t", part.Duration.ToString("0.######", CultureInfo.InvariantCulture), "-map", "0:v:0", "-map", "0:a?",
                    "-filter_threads", "1" };
                args.AddRange(VideoArguments(encoder));
                args.AddRange(["-c:a", "aac", "-b:a", "192k", target]);
                Run("ffmpeg", args, Math.Max(180, (int)(part.Duration * 8)), cancel: cancel);
                files.Add($"file '{i}.mp4'");
            }
            var list = Path.Combine(work, "parts.txt");
            File.WriteAllLines(list, files);
            Run("ffmpeg", ["-hide_banner", "-v", "error", "-n", "-f", "concat", "-safe", "1", "-i", list,
                "-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-movflags", "+faststart", "-f", "mp4", partial], 180, cancel: cancel);
            if (Math.Abs(Duration(partial) - expected) > Math.Max(.25, parts.Count * .05))
                throw new InvalidDataException("Assembled replay duration did not match the fight.");
            if (cancel()) throw new OperationCanceledException();
            File.Move(partial, output);
        }
        finally
        {
            File.Delete(partial);
            Directory.Delete(work, true);
        }
    }

    public static string RemoveGaps(string source, string output, IReadOnlyList<Echo.Core.TimeRange> parts, string? encoder, Func<bool> cancel)
    {
        if (File.Exists(output)) { _ = Duration(output); return output; }
        var temporaryId = Guid.NewGuid().ToString("N");
        var partial = output + "." + temporaryId + ".partial"; var filterFile = output + "." + temporaryId + ".filter";
        var filters = new List<string>(); var inputs = new System.Text.StringBuilder();
        int audioCount = AudioStreamCount(source);
        var seek = parts[0].Start;
        var readDuration = parts[^1].End - seek;
        for (int i = 0; i < parts.Count; i++)
        {
            var range = parts[i]; var from = (range.Start - seek).ToString("0.######", CultureInfo.InvariantCulture); var to = (range.End - seek).ToString("0.######", CultureInfo.InvariantCulture);
            filters.Add($"[0:v:0]trim=start={from}:end={to},setpts=PTS-STARTPTS[v{i}]");
            inputs.Append($"[v{i}]");
            for (int track = 0; track < audioCount; track++)
            {
                filters.Add($"[0:a:{track}]atrim=start={from}:end={to},asetpts=PTS-STARTPTS[a{i}_{track}]");
                inputs.Append($"[a{i}_{track}]");
            }
        }
        var audioLabels = Enumerable.Range(0, audioCount).Select(track => $"[out{track}]").ToArray();
        filters.Add($"{inputs}concat=n={parts.Count}:v=1:a={audioCount}[v]{string.Concat(audioLabels)}");
        try
        {
            File.WriteAllText(filterFile, string.Join(';', filters));
            Run("ffmpeg", new[] { "-hide_banner", "-v", "error", "-n", "-threads", "1", "-ss", seek.ToString("0.######", CultureInfo.InvariantCulture), "-t", readDuration.ToString("0.######", CultureInfo.InvariantCulture), "-i", source, "-filter_complex_threads", "1", "-filter_complex_script", filterFile, "-map", "[v]" }
                .Concat(audioLabels.SelectMany(label => new[] { "-map", label }))
                .Concat(VideoArguments(encoder)).Concat(["-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-f", "mp4", partial]), 180, cancel: cancel);
            var duration = Duration(partial);
            if (Math.Abs(duration - parts.Sum(p => p.Duration)) > .25) throw new InvalidDataException("Prepared clip duration did not match the visible footage.");
            File.Move(partial, output); return output;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); if (File.Exists(filterFile)) File.Delete(filterFile); }
    }
}
