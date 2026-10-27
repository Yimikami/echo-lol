using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Core;

[JsonConverter(typeof(JsonStringEnumConverter<RecordingMode>))]
public enum RecordingMode { Clips, FullMatch }
[JsonConverter(typeof(JsonStringEnumConverter<MomentKind>))]
public enum MomentKind { Kill, Assist, Death, Manual }
[JsonConverter(typeof(JsonStringEnumConverter<CaptureMethod>))]
public enum CaptureMethod { Window, FullscreenDisplay }

public sealed record EchoSettings
{
    public RecordingMode Mode { get; init; } = RecordingMode.Clips;
    public CaptureMethod CaptureMethod { get; init; } = CaptureMethod.Window;
    public string OutputDirectory { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Echo");
    public string? ObsDirectory { get; init; }
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public int Fps { get; init; } = 60;
    public int BitrateKbps { get; init; } = 12000;
    public int BeforeSeconds { get; init; } = 15;
    public int AfterSeconds { get; init; } = 8;
    public int BufferSeconds { get; init; } = 40;
    public int BufferMegabytes { get; init; } = 128;
    public bool GameAudio { get; init; } = true;
    public bool DesktopAudio { get; init; }
    public bool Microphone { get; init; }
    public bool CaptureKills { get; init; } = true;
    public bool CaptureAssists { get; init; } = true;
    public bool CaptureDeaths { get; init; } = true;
    public long MinimumFreeMegabytes { get; init; } = 2048;
    public string Encoder { get; init; } = "auto";

    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) throw new ArgumentException("Unknown recording mode.");
        if (!Enum.IsDefined(CaptureMethod)) throw new ArgumentException("Unknown capture method.");
        if (Width is < 640 or > 3840 || Height is < 360 or > 2160 || Width % 2 != 0 || Height % 2 != 0)
            throw new ArgumentException("Resolution must be even and between 640×360 and 3840×2160.");
        if (Fps is not (30 or 60)) throw new ArgumentException("Choose 30 or 60 FPS.");
        if (BitrateKbps is < 2000 or > 50000) throw new ArgumentException("Bitrate must be between 2,000 and 50,000 Kbps.");
        if (BeforeSeconds is < 1 or > 60 || AfterSeconds is < 0 or > 30)
            throw new ArgumentException("Use 1–60 seconds before and 0–30 seconds after an event.");
        if (BufferSeconds < BeforeSeconds + AfterSeconds + 4 || BufferSeconds > 120)
            throw new ArgumentException("The buffer must cover the clip plus a four-second safety margin, up to 120 seconds.");
        if (BufferMegabytes is < 32 or > 512) throw new ArgumentException("Buffer memory must be 32–512 MB.");
        // Reserve headroom for variable packet sizes, audio and keyframe boundaries.
        if (BufferMegabytes * 1024d * 1024 < BitrateKbps * 1000d / 8 * BufferSeconds * 1.25)
            throw new ArgumentException("Buffer memory is too small for the selected bitrate and duration.");
        if (MinimumFreeMegabytes < 256) throw new ArgumentException("Keep at least 256 MB free.");
        if (string.IsNullOrWhiteSpace(OutputDirectory) || !Path.IsPathFullyQualified(OutputDirectory))
            throw new ArgumentException("Choose an absolute recording directory.");
        if (Encoder is not ("auto" or "nvenc" or "amf" or "qsv")) throw new ArgumentException("Choose a supported hardware encoder.");
    }
    public bool Captures(MomentKind kind) => kind switch
    {
        MomentKind.Kill => CaptureKills, MomentKind.Assist => CaptureAssists,
        MomentKind.Death => CaptureDeaths, _ => true
    };
}

public sealed record Moment(int EventId, MomentKind Kind, double GameTime, double RecordingTime);
public sealed record ClipRange(double Start, double End, List<Moment> Moments)
{
    public double Duration => End - Start;
    public string Label => string.Join(" · ", Moments.GroupBy(m => m.Kind).Select(g => g.Count() == 1 ? g.Key.ToString() : $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}s"));
}
public sealed record MediaClip(string Id, string Path, double Start, double End, string Label, bool IsVirtual, double RecordingOrigin = 0);
public sealed record Session
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public string Champion { get; set; } = "League of Legends";
    public string? Encoder { get; set; }
    public RecordingMode Mode { get; init; }
    public string? RecordingPath { get; set; }
    public string State { get; set; } = "Recording";
    public string? Error { get; set; }
    public List<Moment> Moments { get; init; } = [];
    public List<MediaClip> Clips { get; init; } = [];
    public double Duration { get; set; }
}

public sealed record RecorderStatus
{
    public bool Armed { get; init; }
    public bool Recording { get; init; }
    public string State { get; init; } = "Waiting for League";
    public string? Error { get; init; }
    public string? Encoder { get; init; }
    public string? SessionId { get; init; }
    public string? NativeVersion { get; init; }
    public double ElapsedSeconds { get; init; }
    public int Frames { get; init; }
    public int DroppedFrames { get; init; }
    public bool? GameAudioHooked { get; init; }
    public bool? GameForeground { get; init; }
    public double CpuPercent { get; init; }
    public double WorkingSetMegabytes { get; init; }
    public double PrivateMegabytes { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static T? Read<T>(string path) => File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : default;
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, true);
    }
}
