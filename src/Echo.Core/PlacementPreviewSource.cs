namespace Echo.Core;

public sealed record PlacementPreviewSource(string Path, double At, string SessionId, string ClipId)
{
    public static PlacementPreviewSource? Latest(IEnumerable<Session> sessions)
    {
        foreach (var session in sessions.Where(s => s.EndedAt != null && !s.PreparationPending && s.DeletedAt == null)
                     .OrderByDescending(s => s.StartedAt))
        {
            foreach (var clip in session.Clips.Where(c => c.DeletedAt == null && !c.VideoUnavailable &&
                         double.IsFinite(c.Start) && double.IsFinite(c.End) && c.Start >= 0 && c.End > c.Start)
                         .OrderByDescending(c => c.RecordingOrigin + c.End))
                if (Ready(clip.Path))
                    return new(clip.Path, clip.Start + Math.Min(3, (clip.End - clip.Start) / 2), session.Id, clip.Id);
            if (Ready(session.RecordingPath) && double.IsFinite(session.Duration) && session.Duration > 0)
                return new(session.RecordingPath!, Math.Min(3, session.Duration / 2), session.Id, session.Id);
        }
        return null;
    }

    private static bool Ready(string? path) => !string.IsNullOrWhiteSpace(path) &&
        !path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
}
