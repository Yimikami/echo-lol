namespace Echo.Core;

public static class PlaybackNavigation
{
    public static double? FileTime(MediaClip clip, double recordingTime)
    {
        if (clip.Segments.Count == 0) return recordingTime - clip.RecordingOrigin;
        foreach (var segment in clip.Segments)
            if (recordingTime >= segment.RecordingStart && recordingTime <= segment.RecordingStart + segment.Duration)
                return segment.FileStart + recordingTime - segment.RecordingStart;
        return null;
    }
    public static double BeforeEvent(MediaClip clip, double recordingTime, double start, double end)
        => Math.Clamp((FileTime(clip, recordingTime) ?? start) - 3, start, Math.Max(start, end));
    public static double BeforeEvent(double recordingTime, double recordingOrigin, double start, double end, double leadIn = 3)
        => Math.Clamp(recordingTime - recordingOrigin - leadIn, start, Math.Max(start, end));
}
