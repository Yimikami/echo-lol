using Echo.Core;

namespace Echo.Recorder;

internal static class MediaPreparation
{
    private sealed record Job(string Metadata);
    public static void Enqueue(HostPaths paths, string metadata, string id) => JsonStore.Write(Path.Combine(paths.Root, "preparation", id + ".json"), new Job(metadata));
    public static void TakeOne(HostPaths paths, Func<bool> cancel)
    {
        var queue = Path.Combine(paths.Root, "preparation");
        if (!Directory.Exists(queue) || cancel()) return;
        var file = Directory.EnumerateFiles(queue, "*.json").FirstOrDefault(); if (file == null) return;
        try
        {
            var job = JsonStore.Read<Job>(file);
            if (job == null || !File.Exists(job.Metadata)) { File.Move(file, file + ".done", true); return; }
            Prepare(job.Metadata, cancel); File.Move(file, file + ".done", true);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Console.Error.WriteLine("Clip preparation retained the original: " + e.Message);
            try { var job = JsonStore.Read<Job>(file); if (job != null && JsonStore.Read<Session>(job.Metadata) is { } session) { session.PreparationPending = false; session.Error = "Clip preparation needs attention: " + e.Message; JsonStore.Write(job.Metadata, session); } File.Move(file, file + ".failed", true); }
            catch (IOException) { }
        }
    }
    internal static void Prepare(string metadata, Func<bool> cancel)
    {
        var session = JsonStore.Read<Session>(metadata) ?? throw new IOException("Session metadata is unavailable.");
        if (session.EndedAt == null) return;
        var folder = Path.GetDirectoryName(metadata)!; var options = session.RecordingOptions ?? new EchoSettings();
        if (session.RecordingPath is { } raw && raw.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) && File.Exists(raw))
        {
            if (cancel()) throw new OperationCanceledException();
            var mp4 = File.Exists(Path.ChangeExtension(raw, ".mp4")) ? Path.ChangeExtension(raw, ".mp4") : MediaTools.Remux(raw, cancel);
            _ = MediaTools.Duration(mp4); session.RecordingPath = mp4;
            for (int i = 0; i < session.Clips.Count; i++) if (session.Clips[i].Path == raw) session.Clips[i] = session.Clips[i] with { Path = mp4 };
            JsonStore.Write(metadata, session);
        }
        if (session.RecordingPath is { } fullMatch && File.Exists(fullMatch))
        {
            if (cancel()) throw new OperationCanceledException();
            MediaTools.Thumbnail(fullMatch, Path.Combine(folder, "match.jpg"), Math.Min(2, Math.Max(0, session.Duration / 2)));
        }
        for (int i = 0; i < session.Clips.Count; i++)
        {
            if (cancel()) throw new OperationCanceledException();
            var clip = session.Clips[i]; if (clip.VideoUnavailable) continue;
            if (clip.Path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
            {
                var mp4 = File.Exists(Path.ChangeExtension(clip.Path, ".mp4")) ? Path.ChangeExtension(clip.Path, ".mp4") : MediaTools.Remux(clip.Path, cancel);
                _ = MediaTools.Duration(mp4); session.Clips[i] = clip = clip with { Path = mp4 }; JsonStore.Write(metadata, session);
            }
            if (options.RemoveMaskedHighlights && clip.Segments.Count == 0 && session.MaskedRanges.Count > 0)
            {
                var from = clip.RecordingOrigin + clip.Start; var to = clip.RecordingOrigin + clip.End;
                var visible = MaskTimeline.Visible(from, to, session.MaskedRanges);
                if (visible.Count == 0) { session.Clips[i] = clip with { VideoUnavailable = true }; JsonStore.Write(metadata, session); continue; }
                if (visible.Sum(v => v.Duration) < to - from - .04)
                {
                    var parts = visible.Select(p => new TimeRange(Math.Max(0, p.Start - clip.RecordingOrigin), p.End - clip.RecordingOrigin)).ToArray();
                    var output = MediaTools.RemoveGaps(clip.Path, Path.Combine(folder, clip.Id + "-visible.mp4"), parts, session.Encoder, cancel);
                    double offset = 0; var segments = new List<MediaSegment>();
                    foreach (var part in visible) { segments.Add(new(part.Start, offset, part.Duration)); offset += part.Duration; }
                    session.Clips[i] = clip = clip with { Path = output, Start = 0, End = offset, IsVirtual = false, Segments = segments, ThumbnailPath = Path.Combine(folder, clip.Id + "-visible.jpg") };
                    JsonStore.Write(metadata, session);
                }
            }
            MediaTools.Thumbnail(clip.Path, clip.ThumbnailPath ?? Path.Combine(folder, clip.Id + ".jpg"), clip.Start + Math.Min(2, (clip.End - clip.Start) / 2));
        }
        session.PreparationPending = false; JsonStore.Write(metadata, session);
    }
}
