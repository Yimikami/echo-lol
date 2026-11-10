using System.Globalization;
using System.IO;
using Echo.Core;
using Echo.Recorder;

namespace Echo.App;

internal enum ExportLayout { Original, Square, Portrait, PortraitHud }
internal sealed record ExportOptions(ExportLayout Layout = ExportLayout.Original, double Focus = .5, double VerticalFocus = .5,
    bool MinimapOnLeft = false, double[]? TrackGains = null, bool KeepTracks = false, bool Overlay = true, bool Mute = false);

internal readonly record struct RecapProgress(int Part, int PartCount, double RenderedSeconds, double TotalSeconds, bool Finalizing = false);

internal static class ClipExport
{
    private static string N(double n) => n.ToString("0.######", CultureInfo.InvariantCulture);

    internal static void Save(MediaClip clip, double start, double end, string? inputs, string? encoder,
        InputOverlayPreset preset, string output, string cache, Func<bool> cancel, ExportOptions? options = null, bool replace = true)
    {
        options ??= new();
        if (!File.Exists(clip.Path) || !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end - start < .1)
            throw new ArgumentException("Choose a valid clip range.");
        if (string.Equals(Path.GetFullPath(clip.Path), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a different file from the original.");
        if (cancel()) throw new OperationCanceledException();
        var prepared = Prepare(clip.Path, inputs, preset, options);
        var partial = output + $".{Guid.NewGuid():N}.partial";
        try
        {
            Render(clip, start, end, encoder, preset, options, prepared, partial, cache, cancel, fastStart: true);
            VerifyDuration(partial, end - start, .3, "The export duration did not match your clip.");
            if (cancel()) throw new OperationCanceledException();
            File.Move(partial, output, replace);
        }
        finally { File.Delete(partial); }
    }

    private sealed record PreparedExport(int Width, int Height, GameplayInputPlayback? Inputs);

    private static PreparedExport Prepare(string path, string? inputLog, InputOverlayPreset preset, ExportOptions options)
    {
        if (!options.KeepTracks && options.TrackGains is { Length: > 0 } gains &&
            (gains.Length != MediaTools.AudioStreamCount(path) || gains.Any(g => !double.IsFinite(g) || g is < 0 or > 2)))
            throw new InvalidDataException("The selected audio mix does not match this recording.");

        var inputs = preset.Enabled && options.Overlay && File.Exists(inputLog)
            ? new GameplayInputPlayback(GameplayInputs.Read(inputLog)) : null;
        var (width, height) = options.Layout switch
        {
            ExportLayout.Portrait or ExportLayout.PortraitHud => (1080, 1920),
            ExportLayout.Square => (1080, 1080),
            _ => inputs != null ? MediaTools.VideoSize(path) : (0, 0)
        };
        return new(width, height, inputs);
    }

    private static void Render(MediaClip clip, double start, double end, string? encoder, InputOverlayPreset preset,
        ExportOptions options, PreparedExport prepared, string output, string cache, Func<bool> cancel, bool fastStart,
        Action<double>? progress = null, int renderThreads = 1)
    {
        if (cancel()) throw new OperationCanceledException();
        Directory.CreateDirectory(cache);
        var ass = Path.Combine(cache, $"{Guid.NewGuid():N}.ass");
        try
        {
            var threads = renderThreads.ToString(CultureInfo.InvariantCulture);
            var args = new List<string> { "-hide_banner", "-v", "error", "-n", "-threads", threads, "-ss", N(start), "-i", clip.Path, "-t", N(end - start) };
            var filters = new List<string>();
            int width = prepared.Width, height = prepared.Height;
            var video = "0:v:0";
            if (options.Layout != ExportLayout.Original)
            {
                if (options.Layout == ExportLayout.PortraitHud)
                {
                    var miniX = options.MinimapOnLeft ? "0" : "iw-ow";
                    filters.Add("[0:v:0]split=3[game][map][hud]");
                    filters.Add($"[game]scale=1080:1370:force_original_aspect_ratio=decrease:force_divisible_by=2,pad=1080:1920:(ow-iw)/2:(1370-ih)/2:color=0x101219[canvas]");
                    filters.Add($"[map]crop=iw*0.23:ih*0.36:{miniX}:ih-oh,scale=350:350:force_original_aspect_ratio=decrease:force_divisible_by=2[mapTile]");
                    filters.Add("[hud]crop=iw*0.46:ih*0.24:(iw-ow)/2:ih-oh,scale=610:350:force_original_aspect_ratio=decrease:force_divisible_by=2[hudTile]");
                    filters.Add("[canvas][hudTile]overlay=x=45:y=1500+(350-h)/2[withHud]");
                    filters.Add("[withHud][mapTile]overlay=x=685:y=1500+(350-h)/2,setsar=1[framed]");
                }
                else
                {
                    filters.Add($"[0:v:0]scale={width}:{height}:force_original_aspect_ratio=increase:force_divisible_by=2," +
                        $"crop={width}:{height}:(iw-ow)*{N(Math.Clamp(options.Focus, 0, 1))}:(ih-oh)*{N(Math.Clamp(options.VerticalFocus, 0, 1))},setsar=1[framed]");
                }
                video = "[framed]";
            }
            if (prepared.Inputs is { } inputs)
            {
                File.WriteAllText(ass, InputOverlayExport.Build(clip, start, end, inputs, true, preset, width, height));
                var escaped = ass.Replace('\\', '/').Replace(":", "\\:").Replace("'", "'\\''");
                var source = video.StartsWith('[') ? video : "[" + video + "]";
                filters.Add($"{source}subtitles=filename='{escaped}'[overlaid]");
                video = "[overlaid]";
            }
            var audio = options.Mute ? "" : options.KeepTracks ? "0:a?" : "0:a:0?";
            var gains = options.TrackGains;
            if (!options.KeepTracks && gains is { Length: > 0 })
            {
                var selected = Enumerable.Range(0, gains.Length).Where(i => gains[i] > 0).ToArray();
                if (selected.Length == 0) audio = "";
                else
                {
                    foreach (int i in selected) filters.Add($"[0:a:{i}]volume={N(gains[i])}[gain{i}]");
                    audio = $"[gain{selected[0]}]";
                    if (selected.Length > 1)
                    {
                        filters.Add(string.Concat(selected.Select(i => $"[gain{i}]")) + $"amix=inputs={selected.Length}:normalize=0,alimiter=limit=0.98:level=0[mixed]");
                        audio = "[mixed]";
                    }
                }
            }
            if (filters.Count != 0) args.AddRange(["-filter_complex_threads", threads, "-filter_complex", string.Join(';', filters)]);
            args.AddRange(["-map", video]);
            if (audio.Length > 0) args.AddRange(["-map", audio]);
            args.AddRange(MediaTools.VideoArguments(encoder ?? "software-export"));
            args.AddRange(["-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k"]);
            if (fastStart) args.AddRange(["-movflags", "+faststart"]);
            args.AddRange(["-f", "mp4", output]);
            MediaTools.Run("ffmpeg", args, 900, cancel: cancel, progress: progress);
        }
        finally { File.Delete(ass); }
    }

    internal static void SaveRecap(Session session, IReadOnlyList<TimeRange> ranges, InputOverlayPreset preset,
        string output, string cache, Func<bool> cancel, ExportOptions options, Action<RecapProgress>? report = null)
    {
        var recording = session.RecordingPath;
        if (recording == null || !File.Exists(recording)) throw new IOException("The match video is missing.");
        if (string.Equals(Path.GetFullPath(recording), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a different file from the match recording.");
        if (ranges.Count == 0 || ranges.Any(range => !double.IsFinite(range.Start) || !double.IsFinite(range.End)
                || range.Start < 0 || range.Duration < .1))
            throw new ArgumentException("Choose valid recap ranges.");
        if (cancel()) throw new OperationCanceledException();

        var source = new MediaClip(session.Id, recording, 0, session.Duration, "Full match", true);
        var prepared = Prepare(recording, session.InputLogPath, preset, options);
        var total = ranges.Sum(range => range.Duration);
        var work = Path.Combine(cache, $"recap-{Guid.NewGuid():N}");
        var partial = output + $".{Guid.NewGuid():N}.partial";
        bool single = ranges.Count == 1;
        var list = new List<string>(ranges.Count);
        double completed = 0;
        try
        {
            for (int i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                int part = i + 1;
                var name = $"part-{i}.mp4";
                var target = single ? partial : Path.Combine(work, name);
                double encoded = 0;
                report?.Invoke(new(part, ranges.Count, completed, total));
                Render(source, range.Start, range.End, session.Encoder, preset, options, prepared, target, work, cancel,
                    fastStart: single, progress: seconds =>
                    {
                        if (seconds <= encoded) return;
                        encoded = seconds;
                        report?.Invoke(new(part, ranges.Count, completed + Math.Min(encoded, range.Duration), total));
                    }, renderThreads: 2);
                if (!single && Math.Abs(encoded - range.Duration) > .3)
                    VerifyDuration(target, range.Duration, .3, "A recap segment had an unexpected duration.");
                completed += range.Duration;
                list.Add($"file '{name}'");
            }

            if (cancel()) throw new OperationCanceledException();
            report?.Invoke(new(ranges.Count, ranges.Count, total, total, Finalizing: true));
            if (!single)
            {
                var manifest = Path.Combine(work, "parts.txt");
                File.WriteAllLines(manifest, list);
                MediaTools.Run("ffmpeg", ["-hide_banner", "-v", "error", "-n", "-f", "concat", "-safe", "1", "-i", manifest,
                    "-map", "0", "-c", "copy", "-movflags", "+faststart", "-f", "mp4", partial], 180, cancel: cancel);
            }

            VerifyDuration(partial, total, Math.Max(.35, ranges.Count * .1), "The recap duration did not match its selected moments.");
            if (cancel()) throw new OperationCanceledException();
            File.Move(partial, output, true);
        }
        finally
        {
            File.Delete(partial);
            if (Directory.Exists(work)) Directory.Delete(work, true);
        }
    }

    private static void VerifyDuration(string path, double expected, double tolerance, string error)
    {
        if (Math.Abs(MediaTools.Duration(path) - expected) > tolerance)
            throw new InvalidDataException(error);
    }
}
