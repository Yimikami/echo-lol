namespace Echo.Core;

public static class PlaybackNavigation
{
    public static double BeforeEvent(double recordingTime, double recordingOrigin, double start, double end, double leadIn = 3)
        => Math.Clamp(recordingTime - recordingOrigin - leadIn, start, Math.Max(start, end));
}
