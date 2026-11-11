namespace Echo.Core;

public sealed record CaptureHardware(string GraphicsName, long VideoMemoryMb, long MemoryMb,
    int LogicalProcessors, int DisplayWidth, int DisplayHeight, bool HasHardwareAdapter);

/// <summary>A conservative starting preset, not a gameplay benchmark or encoder capability test.</summary>
public sealed record QualityRecommendation(int Height, int Fps, string Reason)
{
    public int Width => Height == 720 ? 1280 : 1920;
    public int ChoiceIndex => (Height == 720 ? 0 : 2) + (Fps == 60 ? 1 : 0);
    public int BitrateKbps => Bitrate(Height, Fps);
    public string Label => $"{Height}p · {Fps} FPS";

    public static int Bitrate(int height, int fps) => (height, fps) switch
    {
        (720, 30) => 4000, (720, 60) => 6000, (1080, 30) => 8000, _ => 12000
    };

    public static QualityRecommendation For(CaptureHardware pc)
    {
        if (!pc.HasHardwareAdapter || pc.MemoryMb < 7500 || pc.LogicalProcessors < 4)
            return new(720, 30, "A lighter starting point for this PC.");
        bool fullHd = pc.DisplayWidth >= 1920 && pc.DisplayHeight >= 1080;
        if (pc.VideoMemoryMb >= 4000 && pc.MemoryMb >= 15000 && pc.LogicalProcessors >= 8)
            return new(fullHd ? 1080 : 720, 60, "A balanced starting point for your hardware.");
        if (pc.VideoMemoryMb >= 2000 && fullHd)
            return new(1080, 30, "Keep full-HD detail with a lighter frame rate.");
        return new(720, 30, "Lower recording load for shared or limited graphics memory.");
    }
}
