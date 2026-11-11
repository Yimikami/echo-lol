namespace Echo.Core;

public static class ReplayAssembly
{
    public static IReadOnlyList<IReadOnlyList<MediaClip>> Groups(IEnumerable<MediaClip> clips)
    {
        var groups = new List<IReadOnlyList<MediaClip>>();
        var current = new List<MediaClip>();
        double end = 0;
        foreach (var clip in clips.Where(c => !c.IsVirtual && !c.VideoUnavailable && c.DeletedAt == null && c.Segments.Count == 0)
                     .OrderBy(c => c.RecordingOrigin + c.Start))
        {
            var start = clip.RecordingOrigin + clip.Start;
            if (current.Count > 0 && start > end + MomentPlanner.MergeGapSeconds)
            {
                if (current.Count > 1) groups.Add(current);
                current = [];
            }
            end = current.Count == 0 ? clip.RecordingOrigin + clip.End : Math.Max(end, clip.RecordingOrigin + clip.End);
            current.Add(clip);
        }
        if (current.Count > 1) groups.Add(current);
        return groups;
    }

    public static IReadOnlyList<ReplayPart> Parts(IReadOnlyList<MediaClip> clips, Func<string, double> duration)
    {
        var sources = clips.Select(c => new { Clip = c, End = c.RecordingOrigin + duration(c.Path) }).ToArray();
        var cursor = clips.Min(c => c.RecordingOrigin + c.Start);
        var end = clips.Max(c => c.RecordingOrigin + c.End);
        var parts = new List<ReplayPart>();
        while (cursor < end - .001)
        {
            // Prefer the buffer that covers the most remaining footage. Overlap is emitted only once.
            var source = sources.Where(s => s.Clip.RecordingOrigin <= cursor + .001 && s.End > cursor + .001)
                .OrderByDescending(s => s.End).FirstOrDefault()
                ?? throw new InvalidDataException("The replay buffers do not cover the complete fight.");
            var next = Math.Min(end, source.End);
            parts.Add(new(source.Clip.Path, Math.Max(0, cursor - source.Clip.RecordingOrigin), next - source.Clip.RecordingOrigin));
            cursor = next;
        }
        return parts;
    }
}

public sealed record ReplayPart(string Path, double Start, double End)
{
    public double Duration => End - Start;
}
