using System.Globalization;
using System.IO;
using Echo.Core;
using Echo.Recorder;

namespace Echo.App;

internal enum ExportLayout { Original, Square, Portrait, PortraitHud }
internal sealed record ExportOptions(ExportLayout Layout = ExportLayout.Original, double Focus = .5, double VerticalFocus = .5,
    bool MinimapOnLeft = false, double[]? TrackGains = null, bool KeepTracks = false, bool Overlay = true, bool Mute = false);

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
        Directory.CreateDirectory(cache);
        var id = Guid.NewGuid().ToString("N");
        var ass = Path.Combine(cache, id + ".ass");
        var partial = output + "." + id + ".partial";
        try
        {
            var args = new List<string> { "-hide_banner", "-v", "error", "-n", "-threads", "1", "-ss", N(start), "-i", clip.Path, "-t", N(end - start) };
            var filters = new List<string>();
            var (width, height) = options.Layout switch
            {
                ExportLayout.Portrait or ExportLayout.PortraitHud => (1080, 1920),
                ExportLayout.Square => (1080, 1080),
                _ => MediaTools.VideoSize(clip.Path)
            };
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
            if (preset.Enabled && options.Overlay && File.Exists(inputs))
            {
                File.WriteAllText(ass, InputOverlayExport.Build(clip, start, end, inputs, preset, width, height));
                var escaped = ass.Replace('\\', '/').Replace(":", "\\:").Replace("'", "'\\''");
                var source = video.StartsWith('[') ? video : "[" + video + "]";
                filters.Add($"{source}subtitles=filename='{escaped}'[overlaid]");
                video = "[overlaid]";
            }
            var audio = options.Mute ? "" : options.KeepTracks ? "0:a?" : "0:a:0?";
            var gains = options.TrackGains;
            if (!options.KeepTracks && gains is { Length: > 0 })
            {
                if (gains.Length != MediaTools.AudioStreamCount(clip.Path) || gains.Any(g => !double.IsFinite(g) || g is < 0 or > 2))
                    throw new InvalidDataException("The selected audio mix does not match this recording.");
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
            if (filters.Count != 0) args.AddRange(["-filter_complex_threads", "1", "-filter_complex", string.Join(';', filters)]);
            args.AddRange(["-map", video]);
            if (audio.Length > 0) args.AddRange(["-map", audio]);
            args.AddRange(MediaTools.VideoArguments(encoder ?? "software-export"));
            args.AddRange(["-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-f", "mp4", partial]);
            MediaTools.Run("ffmpeg", args, 900, cancel: cancel);
            if (Math.Abs(MediaTools.Duration(partial) - (end - start)) > .3)
                throw new InvalidDataException("The export duration did not match your clip.");
            if (cancel()) throw new OperationCanceledException();
            File.Move(partial, output, replace);
        }
        finally
        {
            File.Delete(partial);
            File.Delete(ass);
        }
    }

    internal static void SaveRecap(Session session, IReadOnlyList<TimeRange> ranges, InputOverlayPreset preset,
        string output, string cache, Func<bool> cancel, ExportOptions options)
    {
        var recording = session.RecordingPath ?? throw new IOException("The match video is missing.");
        if (string.Equals(Path.GetFullPath(recording), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a different file from the match recording.");
        var work = Path.Combine(cache, $"recap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        var partial = output + $".{Guid.NewGuid():N}.partial";
        try
        {
            var source = new MediaClip(session.Id, recording, 0, session.Duration, "Full match", true);
            var list = new List<string>();
            for (int i = 0; i < ranges.Count; i++)
            {
                var segment = $"part-{i}.mp4";
                Save(source, ranges[i].Start, ranges[i].End, session.InputLogPath, session.Encoder, preset,
                    Path.Combine(work, segment), work, cancel, options, false);
                list.Add($"file '{segment}'");
            }
            var manifest = Path.Combine(work, "parts.txt");
            File.WriteAllLines(manifest, list);
            MediaTools.Run("ffmpeg", ["-hide_banner", "-v", "error", "-n", "-f", "concat", "-safe", "1", "-i", manifest,
                "-map", "0", "-c", "copy", "-movflags", "+faststart", "-f", "mp4", partial], 180, cancel: cancel);
            var tolerance = Math.Max(.35, ranges.Count * .1);
            if (Math.Abs(MediaTools.Duration(partial) - ranges.Sum(r => r.Duration)) > tolerance)
                throw new InvalidDataException("The recap duration did not match its selected moments.");
            if (cancel()) throw new OperationCanceledException();
            File.Move(partial, output, true);
        }
        finally
        {
            File.Delete(partial);
            Directory.Delete(work, true);
        }
    }
}
