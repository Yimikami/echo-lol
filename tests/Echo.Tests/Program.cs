using System.Text.Json;
using Echo.Core;

int passed = 0, failed = 0;
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
void Test(string name, Action body) { try { body(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + e.Message); } }
void Throws(Action body) { bool thrown = false; try { body(); } catch (ArgumentException) { thrown = true; } Check(thrown); }
EchoSettings Settings() => new() { OutputDirectory = Path.GetTempPath() };
JsonDocument Events(params object[] events) => JsonDocument.Parse(JsonSerializer.Serialize(new { Events = events }));
object Kill(int id, double time, string killer = "Echo", string victim = "Enemy", string[]? assists = null) => new { EventID = id, EventName = "ChampionKill", EventTime = time, KillerName = killer, VictimName = victim, Assisters = assists ?? [] };
EventDetector Detector()
{
    var detector = new EventDetector();
    using var initial = JsonDocument.Parse("{\"activePlayer\":{\"riotId\":\"Echo#123\",\"riotIdGameName\":\"Echo\"},\"allPlayers\":[{\"riotId\":\"Echo#123\",\"summonerName\":\"Legacy\"}],\"Events\":[]}");
    detector.Identify(initial.RootElement); detector.Consume(initial.RootElement, 0, 0, true); return detector;
}
Test("quality recommendation is conservative for unknown and shared graphics", () =>
{
    Check(QualityRecommendation.For(new("Unknown", 0, 0, 2, 0, 0, false)) is { Height: 720, Fps: 30 });
    Check(QualityRecommendation.For(new("Integrated", 128, 16000, 12, 1920, 1080, true)) is { Height: 720, Fps: 30 });
});
Test("quality recommendation respects display and memory limits", () =>
{
    Check(QualityRecommendation.For(new("GPU", 8000, 32000, 12, 2560, 1440, true)) is { Height: 1080, Fps: 60 });
    Check(QualityRecommendation.For(new("GPU", 8000, 32000, 12, 1366, 768, true)) is { Height: 720, Fps: 60 });
    Check(QualityRecommendation.For(new("GPU", 2000, 8000, 4, 1920, 1080, true)) is { Height: 1080, Fps: 30 });
    Check(QualityRecommendation.For(new("GPU", 8000, 4000, 4, 1920, 1080, true)) is { Height: 720, Fps: 30 });
});
Test("every suggested quality produces valid recording settings", () =>
{
    foreach (var height in new[] { 720, 1080 })
    foreach (var fps in new[] { 30, 60 })
    {
        var q = new QualityRecommendation(height, fps, "");
        (Settings() with { Width = q.Width, Height = height, Fps = fps, BitrateKbps = q.BitrateKbps }).FitReplayBuffer().Validate();
    }
});
Test("event toggles follow global clip lengths without losing custom lengths", () =>
{
    var settings = (Settings() with { HighlightRules = new() { [MomentKind.Dragon] = new(true, 30, 12) } })
        .WithCaptureChoices([new(MomentKind.Baron, false), new(MomentKind.Dragon, false)]);
    settings = settings with { BeforeSeconds = 22, AfterSeconds = 9 };
    Check(settings.Rule(MomentKind.Baron) is { Enabled: false, Before: 22, After: 9 });
    Check(settings.Rule(MomentKind.Dragon) is { Enabled: false, Before: 30, After: 12 });
    settings = settings.WithCaptureChoices([new(MomentKind.Baron, true), new(MomentKind.Dragon, true)]);
    Check(settings.Rule(MomentKind.Baron) is { Enabled: true, Before: 22 });
    Check(settings.Rule(MomentKind.Dragon) is { Enabled: true, Before: 30 });
    settings.FitReplayBuffer().Validate();
});
Test("all event choices persist through settings serialization", () =>
{
    var choices = Enum.GetValues<MomentKind>().Where(k => k != MomentKind.Manual).Select(k => new KeyValuePair<MomentKind, bool>(k, false));
    var settings = JsonSerializer.Deserialize<EchoSettings>(JsonSerializer.Serialize(Settings().WithCaptureChoices(choices), JsonStore.Options), JsonStore.Options)!;
    Check(choices.All(pair => !settings.Captures(pair.Key)) && settings.Captures(MomentKind.Manual));
    Check(!settings.CaptureKills && !settings.CaptureAssists && !settings.CaptureDeaths);
});
Test("placement preview chooses newest ready footage independent of library ordering", () =>
{
    var file = Path.Combine(Path.GetTempPath(), "echo-preview-" + Guid.NewGuid().ToString("N") + ".mp4");
    File.WriteAllText(file, "source-selection fixture");
    try
    {
        var older = new Session { Id = "old", StartedAt = DateTimeOffset.UtcNow.AddDays(-2), EndedAt = DateTimeOffset.UtcNow };
        older.Clips.Add(new("old-clip", file, 0, 10, "Kill", false));
        var latest = new Session { Id = "new", StartedAt = DateTimeOffset.UtcNow.AddDays(-1), EndedAt = DateTimeOffset.UtcNow };
        latest.Clips.AddRange([new("earlier", file, 0, 12, "Assist", false), new("later", file, 30, 50, "Kill", false)]);
        Check(PlacementPreviewSource.Latest([older, latest]) is { SessionId: "new", ClipId: "later", At: 33 });
        latest.PreparationPending = true;
        Check(PlacementPreviewSource.Latest([latest, older])?.SessionId == "old");
        latest.PreparationPending = false; latest.DeletedAt = DateTimeOffset.UtcNow;
        Check(PlacementPreviewSource.Latest([latest, older])?.SessionId == "old");
        older.Clips[0] = older.Clips[0] with { VideoUnavailable = true };
        Check(PlacementPreviewSource.Latest([older, latest]) == null);
    }
    finally { File.Delete(file); }
});
Test("placement preview can use a completed full match and keeps short seeks in range", () =>
{
    var file = Path.Combine(Path.GetTempPath(), "echo-preview-" + Guid.NewGuid().ToString("N") + ".mp4");
    File.WriteAllText(file, "source-selection fixture");
    try
    {
        var match = new Session { Id = "match", EndedAt = DateTimeOffset.UtcNow, RecordingPath = file, Duration = 1 };
        Check(PlacementPreviewSource.Latest([match]) is { ClipId: "match", At: .5 });
        match.Clips.Add(new("short", file, .2, .4, "Assist", false));
        Check(Math.Abs(PlacementPreviewSource.Latest([match])!.At - .3) < .001);
        match.EndedAt = null;
        Check(PlacementPreviewSource.Latest([match]) == null);
    }
    finally { File.Delete(file); }
});
Test("twenty seconds of 1000 Hz scrubbing never queue intermediate decoder seeks", () =>
{
    var scrub = new PlaybackScrub(); scrub.Begin(0);
    int previews = 0;
    for (int i = 1; i <= 20000; i++)
    {
        scrub.Request(i % 1800 + .125, i / 1000d);
        if (scrub.TakePreview(i / 1000d) != null) previews++;
    }
    Check(previews == 0 && scrub.Complete() == 20000 % 1800 + .125);
    Check(!scrub.Active && scrub.TakePreview(100) == null && scrub.Complete() == null);
});
Test("a paused pointer previews only its latest target once", () =>
{
    var scrub = new PlaybackScrub(); scrub.Begin(0);
    scrub.Request(800, 0); scrub.Request(17.25, .01); scrub.Request(12.5, .02);
    Check(scrub.TakePreview(.1) == null && scrub.TakePreview(.15) == 12.5);
    Check(scrub.TakePreview(10) == null && scrub.Complete() == 12.5);
});
Test("repeated stationary mouse events do not postpone the frame preview", () =>
{
    var scrub = new PlaybackScrub(); scrub.Begin(0); scrub.Request(42, 0);
    for (int i = 1; i <= 20; i++) scrub.Request(42, i / 100d);
    Check(scrub.TakePreview(.21) == 42);
});
Test("releasing a quick drag commits immediately without waiting for a preview", () =>
{
    var scrub = new PlaybackScrub(); scrub.Begin(10); scrub.Request(3.125, 5);
    Check(scrub.Complete() == 3.125 && scrub.TakePreview(100) == null);
});
Test("cancelled scrubs cannot seek a different recording later", () =>
{
    var scrub = new PlaybackScrub(); scrub.Begin(0); scrub.Request(900, 0); scrub.Cancel();
    Check(scrub.TakePreview(1) == null && scrub.Complete() == null);
    scrub.Begin(4); Check(scrub.TakePreview(2) == null && scrub.Complete() == 4);
});
Test("non-finite scrub requests cannot poison the final seek", () =>
{
    var scrub = new PlaybackScrub(); scrub.Begin(4);
    scrub.Request(double.NaN, 0); scrub.Request(double.PositiveInfinity, 1); scrub.Request(8, double.NaN);
    Check(scrub.Complete() == 4);
});
Test("default audio is game only", () => { var s = Settings(); Check(s.GameAudio && !s.DesktopAudio && !s.Microphone); });
Test("valid defaults", () => Settings().Validate());
Test("reject insufficient replay memory", () => Throws(() => (Settings() with { BufferMegabytes = 32, BitrateKbps = 50000 }).Validate()));
Test("reject undersized buffer", () => Throws(() => (Settings() with { BufferSeconds = 20 }).Validate()));
Test("reject odd resolution", () => Throws(() => (Settings() with { Width = 1919 }).Validate()));
Test("reject software encoder", () => Throws(() => (Settings() with { Encoder = "x264" }).Validate()));
Test("reject invalid FPS", () => Throws(() => (Settings() with { Fps = 144 }).Validate()));
Test("kill uses event time not poll arrival", () => { var d = Detector(); using var e = Events(Kill(1, 10)); var m = d.Consume(e.RootElement, 12, 5).Single(); Check(m.Kind == MomentKind.Kill && m.RecordingTime == 3); });
Test("assist alias matching", () => { var d = Detector(); using var e = Events(Kill(1, 10, "Friend", "Enemy", ["Echo"])); Check(d.Consume(e.RootElement, 10, 10).Single().Kind == MomentKind.Assist); });
Test("death matching full Riot ID", () => { var d = Detector(); using var e = Events(Kill(1, 10, "Enemy", "Echo#123")); Check(d.Consume(e.RootElement, 10, 10).Single().Kind == MomentKind.Death); });
Test("legacy player alias", () => { var d = Detector(); using var e = Events(Kill(1, 10, "Legacy")); Check(d.Consume(e.RootElement, 10, 10).Count == 1); });
Test("ignore other players", () => { var d = Detector(); using var e = Events(Kill(1, 10, "Echo#999")); Check(d.Consume(e.RootElement, 10, 10).Count == 0); });
Test("deduplicate repeated events", () => { var d = Detector(); using var e = Events(Kill(1, 10)); Check(d.Consume(e.RootElement, 10, 10).Count == 1); Check(d.Consume(e.RootElement, 11, 11).Count == 0); });
Test("multiple kills in one response survive", () => { var d = Detector(); using var e = Events(Kill(1, 10), Kill(2, 11)); Check(d.Consume(e.RootElement, 12, 12).Count == 2); });
Test("late start baseline suppresses history", () => { var d = Detector(); using var e = Events(Kill(1, 10)); Check(d.Consume(e.RootElement, 20, 0, true).Count == 0); Check(d.Consume(e.RootElement, 21, 1).Count == 0); });
Test("stale event before capture is ignored", () => { var d = Detector(); using var e = Events(Kill(1, 10)); Check(d.Consume(e.RootElement, 30, 4).Count == 0); });
Test("future event can be retried", () => { var d = Detector(); using var e = Events(Kill(1, 30)); Check(d.Consume(e.RootElement, 10, 10).Count == 0); Check(d.Consume(e.RootElement, 30, 30).Count == 1); });
Test("game clock reset is explicit", () => { var d = Detector(); using var e = Events(); d.Consume(e.RootElement, 100, 100); bool reset = false; try { d.Consume(e.RootElement, 1, 101); } catch (InvalidOperationException) { reset = true; } Check(reset); });
Test("malformed event is skipped", () => { var d = Detector(); using var e = JsonDocument.Parse("{\"Events\":[{\"EventID\":\"bad\"},{\"EventID\":2,\"EventName\":\"ChampionKill\",\"EventTime\":\"bad\"}]}"); Check(d.Consume(e.RootElement, 10, 10).Count == 0); });
Test("overlapping moments merge", () => { var p = new MomentPlanner(Settings()); p.Add(new(1, MomentKind.Kill, 20, 20)); p.Add(new(2, MomentKind.Assist, 25, 25)); Check(p.Ranges.Count == 1 && p.Ranges[0].Start == 5 && p.Ranges[0].End == 33 && p.Ranges[0].Moments.Count == 2); });
Test("separated moments remain separate", () => { var p = new MomentPlanner(Settings()); p.Add(new(1, MomentKind.Kill, 20, 20)); p.Add(new(2, MomentKind.Kill, 100, 100)); Check(p.Ranges.Count == 2); });
Test("disabled event does not schedule", () => { var p = new MomentPlanner(Settings() with { CaptureAssists = false }); p.Add(new(1, MomentKind.Assist, 20, 20)); Check(p.Ranges.Count == 0); });
Test("planner clamps start at zero", () => { var p = new MomentPlanner(Settings()); p.Add(new(1, MomentKind.Kill, 1, 1)); Check(p.Ranges[0].Start == 0); });
Test("scheduler waits for tail", () => { var p = new ReplayScheduler(Settings()); p.Add(new(1, MomentKind.Kill, 20, 20)); Check(p.TakeDue(27) == null); Check(p.TakeDue(28)?.End == 28); Check(p.Pending.Count == 0); });
Test("late events join a previously saved replay without duplicating overlap", () =>
{
    var scheduler = new ReplayScheduler(Settings() with { BeforeSeconds = 1, AfterSeconds = 5 });
    scheduler.Add(new(1, MomentKind.Assist, 20, 20));
    var first = scheduler.TakeDue(25)!;
    scheduler.Add(new(2, MomentKind.Assist, 28, 28));
    var second = scheduler.TakeDue(33)!;
    MediaClip[] clips = [new("a", "a.mp4", first.Start, first.End, first.Label, false),
        new("b", "b.mp4", second.Start - 10, second.End - 10, second.Label, false, 10)];
    var group = ReplayAssembly.Groups(clips).Single();
    var parts = ReplayAssembly.Parts(group, path => path == "a.mp4" ? 25 : 23);
    Check(parts.Sum(p => p.Duration) == 14 && parts.Count == 1);
    var planner = new MomentPlanner(Settings() with { BeforeSeconds = 1, AfterSeconds = 5 });
    planner.Add(new(1, MomentKind.Assist, 20, 20));
    planner.Add(new(2, MomentKind.Kill, 28, 28));
    Check(planner.Ranges.Single().Start == 19 && planner.Ranges.Single().End == 33);
});
Test("long replay fights retain their beginning and distant fights remain separate", () =>
{
    MediaClip[] clips = [new("a", "a.mp4", 5, 38, "Kill", false),
        new("b", "b.mp4", 1, 30, "Assist", false, 36), new("c", "c.mp4", 0, 20, "Death", false, 90)];
    var group = ReplayAssembly.Groups(clips).Single();
    var parts = ReplayAssembly.Parts(group, _ => 40);
    Check(group.Count == 2 && parts.Count == 2 && parts.Sum(p => p.Duration) == 61);
    Check(parts[0].Start == 5 && parts[0].End == 40 && parts[1].Start == 4 && parts[1].End == 30);
    Check(ReplayAssembly.Groups([clips[0], clips[1] with { DeletedAt = DateTimeOffset.UtcNow }]).Count == 0);
});
Test("missing replay coverage is rejected before replacing original clips", () =>
{
    MediaClip[] clips = [new("a", "a.mp4", 0, 10, "Kill", false), new("b", "b.mp4", 0, 10, "Assist", false, 12)];
    bool failed = false;
    try { ReplayAssembly.Parts(clips, _ => 10); } catch (InvalidDataException) { failed = true; }
    Check(failed);
});
Test("force flush clamps unavailable tail", () => { var p = new ReplayScheduler(Settings()); p.Add(new(1, MomentKind.Kill, 20, 20)); Check(p.TakeDue(22, true)?.End == 22); Check(p.Pending.Count == 0); });
Test("long fight is split before buffer eviction", () => { var p = new ReplayScheduler(Settings()); for (int i = 20; i <= 45; i += 5) p.Add(new(i, MomentKind.Kill, i, i)); var part = p.TakeDue(42); Check(part != null && part.Start == 5 && part.End == 42); Check(p.Pending.Single().Start == 41); Check(p.TakeDue(53) != null); });
Test("atomic session roundtrip", () => { var folder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "echo-tests-" + Guid.NewGuid().ToString("N")); try { var path = Path.Combine(folder, "session.json"); var session = new Session(); JsonStore.Write(path, session); Check(JsonStore.Read<Session>(path)?.Id == session.Id); Check(!File.Exists(path + ".tmp")); } finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); } });
Test("metadata replacement recovers when a Windows reader releases its short lock", () =>
{
    if (!OperatingSystem.IsWindows()) return;
    var folder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "echo-lock-" + Guid.NewGuid().ToString("N"));
    try
    {
        var path = Path.Combine(folder, "session.json");
        JsonStore.Write(path, new Session { Champion = "Before" });
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var release = Task.Run(async () => { await Task.Delay(100); reader.Dispose(); });
        JsonStore.Write(path, new Session { Champion = "After" });
        release.GetAwaiter().GetResult();
        Check(JsonStore.Read<Session>(path)?.Champion == "After" && Directory.GetFiles(folder, "*.tmp").Length == 0);
    }
    finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
});
Test("persistent metadata lock preserves the original and fails within a bounded time", () =>
{
    if (!OperatingSystem.IsWindows()) return;
    var folder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "echo-lock-" + Guid.NewGuid().ToString("N"));
    try
    {
        var path = Path.Combine(folder, "session.json");
        JsonStore.Write(path, new Session { Champion = "Original" });
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Exception? failure = null; var clock = System.Diagnostics.Stopwatch.StartNew();
        try { JsonStore.Write(path, new Session { Champion = "Replacement" }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failure = error; }
        Check(failure != null && clock.Elapsed < TimeSpan.FromSeconds(5) && failure.Message.Contains("session.json") &&
            JsonStore.Read<Session>(path)?.Champion == "Original" && Directory.GetFiles(folder, "*.tmp").Length == 0);
    }
    finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
});
Test("read-only metadata is not silently changed or bypassed", () =>
{
    if (!OperatingSystem.IsWindows()) return;
    var folder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "echo-readonly-" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(folder, "session.json");
    try
    {
        JsonStore.Write(path, new Session { Champion = "Original" }); File.SetAttributes(path, FileAttributes.ReadOnly);
        bool denied = false;
        try { JsonStore.Write(path, new Session { Champion = "Replacement" }); }
        catch (UnauthorizedAccessException) { denied = true; }
        Check(denied && JsonStore.Read<Session>(path)?.Champion == "Original" && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
    }
    finally { if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
});
void LibraryFixture(Action<string, string, string, Session> body)
{
    var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "library-test-" + Guid.NewGuid().ToString("N")));
    var session = new Session { EndedAt = DateTimeOffset.UtcNow, State = "Complete", Mode = RecordingMode.FullMatch };
    var folder = Path.Combine(root, session.Id); Directory.CreateDirectory(folder);
    var media = Path.Combine(folder, "match.mp4"); File.WriteAllText(media, "private test fixture");
    session.RecordingPath = media;
    session.Clips.Add(new("one", media, 0, 10, "Kill", true)); session.Clips.Add(new("two", media, 10, 20, "Assist", true));
    var metadata = Path.Combine(folder, "session.json"); JsonStore.Write(metadata, session);
    try { body(root, media, metadata, session); }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
Test("whole-match Trash can be restored with dependent highlights", () => LibraryFixture((root, media, metadata, s) =>
{
    var match = new LibraryEntry(s.Id, s.Id, true);
    LibraryCatalog.SetTrash(root, [match], true);
    var trashed = JsonStore.Read<Session>(metadata)!;
    Check(trashed.DeletedAt.HasValue && trashed.Clips.Count == 2 && File.Exists(media));
    LibraryCatalog.SetTrash(root, [match], false);
    var restored = JsonStore.Read<Session>(metadata)!;
    Check(restored.DeletedAt == null && restored.Clips.Count == 2 && restored.RecordingPath == media);
}));
Test("failed purge keeps the Trash entry available for retry", () => LibraryFixture((root, media, metadata, s) =>
{
    LibraryCatalog.SetTrash(root, [new(s.Id, s.Id, true)], true);
    bool failed = false;
    try { LibraryCatalog.Purge(root, s.Id, _ => throw new IOException("fixture purge failed")); }
    catch (IOException) { failed = true; }
    Check(failed && File.Exists(media) && JsonStore.Read<Session>(metadata)?.DeletedAt != null);
}));
Test("active and preparing recordings cannot be moved to Trash", () => LibraryFixture((root, media, metadata, s) =>
{
    var match = new LibraryEntry(s.Id, s.Id, true);
    foreach (var (end, pending) in new (DateTimeOffset?, bool)[] { (null, false), (DateTimeOffset.UtcNow, true) })
    {
        s.EndedAt = end;
        s.PreparationPending = pending;
        JsonStore.Write(metadata, s);
        bool blocked = false;
        try { LibraryCatalog.SetTrash(root, [match], true); }
        catch (InvalidOperationException) { blocked = true; }
        Check(blocked && File.Exists(media) && JsonStore.Read<Session>(metadata)?.DeletedAt == null);
    }
}));
Test("purging a standalone clip removes its unshared file", () => LibraryFixture((root, media, metadata, s) =>
{
    s.RecordingPath = null;
    s.Clips.RemoveAll(clip => clip.Id == "two");
    JsonStore.Write(metadata, s);
    LibraryCatalog.SetTrash(root, [new(s.Id, "one", false)], true);
    LibraryCatalog.Purge(root, s.Id, File.Delete);
    Check(!File.Exists(media) && JsonStore.Read<Session>(metadata)!.Clips.Count == 0);
}));
Test("purging a match leaves files outside the recording folder intact", () => LibraryFixture((root, media, metadata, s) =>
{
    var unrelated = Path.Combine(root, "unrelated.mp4");
    File.WriteAllText(unrelated, "unrelated fixture");
    s.RecordingPath = unrelated;
    JsonStore.Write(metadata, s);
    LibraryCatalog.SetTrash(root, [new(s.Id, s.Id, true)], true);
    LibraryCatalog.Purge(root, s.Id, File.Delete);
    Check(!File.Exists(media) && !File.Exists(metadata) && File.Exists(unrelated));
}));
Test("moment preview includes three second lead in", () => Check(PlaybackNavigation.BeforeEvent(15, 0, 0, 25) == 12));
Test("moment preview maps replay origin", () => Check(PlaybackNavigation.BeforeEvent(115, 100, 0, 25) == 12));
Test("moment preview clamps to available clip", () => Check(PlaybackNavigation.BeforeEvent(2, 0, 0, 25) == 0));
Test("moment preview respects trim start", () => Check(PlaybackNavigation.BeforeEvent(15, 0, 14, 25) == 14));
Test("manual shortcut has canonical modifiers", () => { var key = ShortcutGesture.Parse("shift+ctrl+f8")!; Check(key.Text == "Ctrl+Shift+F8" && key.Modifiers == 6 && key.Key == 0x77); });
Test("manual shortcut can be disabled", () => Check(ShortcutGesture.Parse("") == null));
Test("plain gameplay keys are rejected", () => Throws(() => ShortcutGesture.Parse("Q")));
Test("reserved F12 is rejected", () => Throws(() => ShortcutGesture.Parse("Ctrl+F12")));
Test("duplicate shortcut modifier is rejected", () => Throws(() => ShortcutGesture.Parse("Ctrl+Ctrl+S")));
Test("default manual shortcut validates", () => Check(ShortcutGesture.Parse(new EchoSettings().ManualShortcut) != null));
Test("mask intervals follow actual state transitions", () => { var m = new MaskTimeline(); m.Update(0, false); m.Update(2, true); m.Update(4, true); m.Update(6, false); var r = m.Snapshot(10).Single(); Check(r.Start == 2 && r.End == 6); });
Test("open mask interval ends with the session", () => { var m = new MaskTimeline(); m.Update(5, true); Check(m.Snapshot(9).Single().End == 9); });
Test("unmasked highlight stays unchanged", () => { var r = MaskTimeline.Visible(5, 15, []); Check(r.Single() == new TimeRange(5, 15)); });
Test("masked middle is removed with boundary guard", () => { var r = MaskTimeline.Visible(0, 10, [new(2, 4)]); Check(r.Count == 2 && Math.Abs(r[0].End - 1.94) < .001 && Math.Abs(r[1].Start - 4.06) < .001); });
Test("fully masked highlight has no visible range", () => Check(MaskTimeline.Visible(2, 4, [new(0, 10)]).Count == 0));
Test("overlapping masks do not duplicate footage", () => { var r = MaskTimeline.Visible(0, 10, [new(2, 5), new(4, 7)], 0); Check(r.SequenceEqual([new TimeRange(0, 2), new TimeRange(7, 10)])); });
Test("masks outside a highlight are ignored", () => Check(MaskTimeline.Visible(10, 20, [new(0, 5), new(25, 30)]).Single() == new TimeRange(10, 20)));
Test("cleaned event maps through discontinuous segments", () => { var c = new MediaClip("a", "x", 0, 15, "Kill", false) { Segments = [new(0, 0, 5), new(10, 5, 10)] }; Check(PlaybackNavigation.FileTime(c, 12) == 7 && PlaybackNavigation.BeforeEvent(c, 12, 0, 15) == 4); });
Test("event inside removed video is not a false marker", () => { var c = new MediaClip("a", "x", 0, 15, "Kill", false) { Segments = [new(0, 0, 5), new(10, 5, 10)] }; Check(PlaybackNavigation.FileTime(c, 8) == null); });
Test("gameplay inputs default to off", () => Check(!Settings().RecordInputs));
Test("input APM excludes releases and presses older than sixty seconds", () => Check(GameplayInputs.Apm([new(1,"Q",true),new(2,"Q",false),new(60,"MouseRight",true),new(65,"W",true)], 64) == 1));
Test("input hold state follows releases", () => Check(GameplayInputs.Held([new(1,"Q",true),new(2,"W",true),new(3,"Q",false)], 4).SetEquals(["W"])));
Test("cleaned input time maps to the correct original segment boundary", () => { var c = new MediaClip("x", "x", 0, 8, "", false) { Segments = [new(10,0,3),new(20,3,5)] }; Check(GameplayInputs.RecordingTime(c,3) == 20 && GameplayInputs.RecordingTime(c,5) == 22); });
Test("replay input mapping includes recording origin", () => Check(GameplayInputs.RecordingTime(new("x","x",0,8,"",false,100),2) == 102));
Test("input loader rejects unrelated controls and interrupted lines", () => { var file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".jsonl"); try { File.WriteAllLines(file,[JsonSerializer.Serialize(new InputSample(1,"Q",true)),JsonSerializer.Serialize(new InputSample(2,"Unrelated",true)),"{incomplete"]); Check(GameplayInputs.Read(file).Single().Control=="Q"); } finally { File.Delete(file); } });
Test("short taps stay visible without increasing APM", () =>
{
    var inputs = new GameplayInputPlayback([new(1, "Q", true), new(1.012, "Q", false), new(2, "RightShift", true), new(3, "RightShift", false)]);
    Check(inputs.Visible(1.14).SetEquals(["Q"]) && inputs.Visible(1.18).Count == 0);
    Check(inputs.Visible(2.9).Contains("RightShift") && inputs.Visible(3).Count == 0);
    Check(inputs.Apm(1.14) == 1 && inputs.Apm(60.999) == 2 && inputs.Apm(61) == 1);
});
Test("rapid scroll pulses keep each action in APM", () =>
{
    var inputs = new GameplayInputPlayback([new(1, "WheelUp", true), new(1.01, "WheelUp", true), new(1.1, "WheelUp", false)]);
    Check(inputs.Apm(1.12) == 2 && inputs.Visible(1.16).Contains("WheelUp") && inputs.Visible(1.18).Count == 0);
});
Test("standard keyboard maps separate modifiers, navigation, numpad and ISO punctuation", () =>
{
    Check(WindowsKeyboard.Control(13, 0x1C, 2) == "NumpadEnter");
    Check(WindowsKeyboard.Control(16, 0x36, 0) == "RightShift");
    Check(WindowsKeyboard.Control(17, 0x1D, 2) == "RightCtrl");
    Check(WindowsKeyboard.Control(37, 0x4B, 0) == "Numpad4" && WindowsKeyboard.Control(37, 0x4B, 2) == "Left");
    Check(WindowsKeyboard.Control(223, 0x56, 0) == "IsoBackslash" && WindowsKeyboard.Control(123, 0x58, 0) == "F12");
    var custom = new InputOverlayPreset { Layout = "Custom", Keys = [.. OverlayAppearance.KeyboardKeys], ShowMouse = false };
    custom.Validate();
    var layout = OverlayAppearance.Layout(custom);
    Check(layout.Keys.Count >= 104 && layout.Keys.All(k => k.X + k.Width <= layout.Width && k.Y + k.Height <= layout.Height));
});
Test("compact and extended mice start at the top row", () =>
{
    foreach (var name in new[] { "Compact", "Full" })
    {
        var layout = OverlayAppearance.Layout(name);
        Check(layout.Mouse!.Y == layout.Keys.Where(k => OverlayAppearance.KeyboardKeys.Contains(k.Id)).Min(k => k.Y));
    }
});
Test("overlay presets reject invalid sizes and positions",()=>{Throws(()=>new InputOverlayPreset{Width=.01}.Validate());Throws(()=>new InputOverlayPreset{X=double.NaN}.Validate());});
Test("overlay presets reject unknown layouts",()=>Throws(()=>new InputOverlayPreset{Layout="unknown"}.Validate()));
Test("all overlay layouts have visible keys within their bounds",()=>{foreach(var name in new[]{"Compact","Full","Mouse"}){var layout=OverlayAppearance.Layout(name);Check(layout.Keys.Count>0&&layout.Keys.All(k=>k.X>=0&&k.Y>=0&&k.X+k.Width<=layout.Width&&k.Y+k.Height<=layout.Height));}});
Test("reference layouts retain extended keys and blank compact keycaps",()=>{var full=OverlayAppearance.Layout("Full");var compact=OverlayAppearance.Layout("Compact");Check(full.Keys.Any(k=>k.Id=="Backtick")&&full.Keys.Any(k=>k.Id=="Z")&&full.Keys.Any(k=>k.Id=="Space"&&k.Label=="")&&compact.Keys.Count(k=>k.Label==""&&!k.Id.StartsWith("Mouse")&&!k.Id.StartsWith("Wheel"))==2&&GameplayInputs.Allowed.Contains("Backtick"));});
Test("custom keys omit unused controls and retain six",()=>{var p=new InputOverlayPreset{Layout="Custom"};p.Validate();var layout=OverlayAppearance.Layout(p);Check(layout.Keys.Any(k=>k.Id=="6")&&!layout.Keys.Any(k=>k.Id=="A")&&p.Keys.All(id=>layout.Keys.Any(k=>k.Id==id)));});
Test("custom keyboard supports A and mouse-free layouts",()=>{var layout=OverlayAppearance.Layout(new InputOverlayPreset{Layout="Custom",Keys=["A","Ctrl","Space"],ShowMouse=false});Check(layout.Mouse==null&&layout.Keys.Select(k=>k.Id).ToHashSet().SetEquals(["A","Ctrl","Space"])&&layout.Keys.All(k=>k.X+k.Width<=layout.Width&&k.Y+k.Height<=layout.Height));});
Test("custom keys reject duplicates and unknown controls",()=>{Throws(()=>new InputOverlayPreset{Keys=["A","A"]}.Validate());Throws(()=>new InputOverlayPreset{Keys=["UnsupportedKey"]}.Validate());});
Test("empty custom keyboard remains valid",()=>{var p=new InputOverlayPreset{Layout="Custom",Keys=[],ShowMouse=false};p.Validate();Check(OverlayAppearance.Layout(p).Keys.Count==0&&OverlayAppearance.Layout(p).Height==38);});
Test("activity chart drops presses after the chosen window",()=>{var chart=InputActivity.Build([new(0,"Q",true),new(10,"Q",false),new(20,"W",true)],new("x","x",0,80,"",false),0,80,windowSeconds:60);Check(chart[0].Apm==1&&chart.Single(p=>p.Time==40).Apm==2&&chart[^1].Apm==0);});
Test("activity chart maps cleaned recording segments",()=>{var clip=new MediaClip("x","x",0,4,"",false){Segments=[new(10,0,2),new(100,2,2)]};var chart=InputActivity.Build([new(11,"Q",true),new(100,"W",true)],clip,0,4);Check(chart[1].Apm==6&&chart[2].Apm==6);});
Test("local activity is a per-minute rate with visible quiet intervals",()=>{var chart=InputActivity.Build([new(0,"Q",true),new(2,"W",true),new(21,"E",true)],new("x","x",0,30,"",false),0,30);Check(chart.Single(p=>p.Time==5).Apm==12&&chart.Single(p=>p.Time==15).Apm==0&&chart.Single(p=>p.Time==25).Apm==6);});
Test("long activity charts stay bounded and include their endpoints",()=>{var chart=InputActivity.Build([],new("x","x",0,7200,"",false),0,7200);Check(chart.Count==512&&chart[0].Time==0&&chart[^1].Time==7200&&chart.All(p=>p.Apm==0));});
Test("legacy settings receive an overlay preset",()=>{var s=JsonSerializer.Deserialize<EchoSettings>("{}",JsonStore.Options)!;Check(s.InputOverlay.Enabled&&s.InputOverlay.Layout=="Compact"&&s.Theme=="Dark");});
Test("full keyboard F keys and navigation are permitted controls",()=>Check(GameplayInputs.Allowed.Contains("F12")&&GameplayInputs.Allowed.Contains("Left")&&GameplayInputs.Allowed.Contains("Z")));
Test("theme selection is validated",()=>Throws(()=>(Settings() with{Theme="unknown"}).Validate()));
Test("exit request is durable and carries a new identity",()=>{var root=Path.Combine(Path.GetTempPath(),"echo-exit-"+Guid.NewGuid());try{var p=new HostPaths(root);Check(ApplicationExit.Current(p)==null);ApplicationExit.Request(p);var first=ApplicationExit.Current(p);Check(first!=null&&File.Exists(p.StopRequest));ApplicationExit.Request(p);Check(ApplicationExit.Current(p)!=first);}finally{Directory.Delete(root,true);}});
Test("Riot multikill, ally ace and objective steals are attributed to the active player", () =>
{
    var detector = Detector();
    using var players = JsonDocument.Parse("""{"activePlayer":{"riotId":"Echo#123","riotIdGameName":"Echo"},"allPlayers":[{"riotId":"Echo#123","team":"ORDER"}]}""");
    detector.Identify(players.RootElement);
    using var events = Events(
        new { EventID = 1, EventTime = 15, EventName = "Multikill", KillerName = "Echo#123", KillStreak = 5 },
        new { EventID = 2, EventTime = 16, EventName = "Ace", Acer = "Ally", AcingTeam = "ORDER" },
        new { EventID = 3, EventTime = 17, EventName = "BaronKill", KillerName = "Echo#123", Stolen = "True" },
        new { EventID = 4, EventTime = 18, EventName = "DragonKill", KillerName = "Ally", Assisters = new[] { "Echo" }, Stolen = true },
        new { EventID = 5, EventTime = 19, EventName = "HeraldKill", KillerName = "Enemy", Stolen = false },
        new { EventID = 6, EventTime = 20, EventName = "InhibKilled", KillerName = "Echo" },
        new { EventID = 7, EventTime = 21, EventName = "TurretKilled", KillerName = "Echo" });
    var moments = detector.Consume(events.RootElement, 21, 19);
    Check(moments.Select(m => m.Kind).SequenceEqual([MomentKind.MultiKill, MomentKind.Ace, MomentKind.Baron, MomentKind.Dragon, MomentKind.Inhibitor, MomentKind.Turret]));
    Check(moments[0].Label == "Pentakill" && moments[0].RecordingTime == 13 && moments[2].Label == "Baron steal" && !moments[3].Stolen);
});
Test("event-specific windows merge even when a later event needs an earlier lead-in", () =>
{
    var settings = (Settings() with { HighlightRules = new() { [MomentKind.Baron] = new(true, 25, 3), [MomentKind.Death] = new(false, 5, 0) } }).FitReplayBuffer();
    settings.Validate();
    var replay = new ReplayScheduler(settings);
    var plan = new MomentPlanner(settings);
    foreach (var moment in new Moment[] { new(1, MomentKind.Kill, 40, 40), new(2, MomentKind.Baron, 49, 49), new(3, MomentKind.Death, 50, 50) })
    { replay.Add(moment); plan.Add(moment); }
    Check(replay.Pending.Single() is { Start: 24, End: 52 } && plan.Ranges.Single().Moments.Count == 2);
    Check(replay.TakeDue(51) == null && replay.TakeDue(52)?.Moments.Count == 2);
});
Test("recap filters events, merges overlap and omits masked periods", () =>
{
    var match = new Session { Duration = 90, MaskedRanges = [new(28, 31)],
        Moments = [new(1, MomentKind.Kill, 25, 25), new(2, MomentKind.Assist, 30, 30), new(3, MomentKind.Death, 75, 75)] };
    var ranges = MatchReview.RecapRanges(match, new HashSet<MomentKind> { MomentKind.Kill, MomentKind.Assist }, Settings());
    Check(ranges.Count == 2 && ranges[0].Start == 10 && ranges[0].End < 28 && ranges[1].Start > 31 && ranges[1].End == 38);
});
Test("match APM counts keydowns inside the recorded duration", () =>
    Check(MatchReview.AverageApm(new Session { Duration = 120 }, [new(10, "Q", true), new(11, "Q", false), new(20, "W", true), new(150, "E", true)]) == 1));
Test("library Trash preserves shared footage and supports batch restore", () =>
{
    var root = Path.Combine(Path.GetTempPath(), $"echo-library-{Guid.NewGuid():N}");
    try
    {
        var (match, path) = ManagedLibraryFixture(root);
        var one = new LibraryEntry(match.Id, "one", false);
        var two = new LibraryEntry(match.Id, "two", false);
        LibraryCatalog.SetTrash(root, [one, two], true);
        Check(LibraryCatalog.Read(root).Single().Clips.All(c => c.DeletedAt.HasValue));
        LibraryCatalog.SetTrash(root, [one], false);
        LibraryCatalog.Purge(root, match.Id, File.Delete);
        Check(File.Exists(path) && LibraryCatalog.Read(root).Single().Clips.Single().Id == "one");
        Check(LibraryCatalog.Usage(root) == new LibraryUsage(128, 0));
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
Test("quota cleanup keeps favorite clips and removes only owned old media", () =>
{
    var root = Path.Combine(Path.GetTempPath(), $"echo-quota-{Guid.NewGuid():N}");
    try
    {
        var (protectedMatch, keep) = ManagedLibraryFixture(root);
        var (_, remove) = ManagedLibraryFixture(root);
        LibraryCatalog.Edit(root, [new(protectedMatch.Id, "one", false)], (_, c) => c! with { Favorite = true });
        Check(LibraryCatalog.Manage(root, 128, File.Delete, () => false) == 1);
        Check(File.Exists(keep) && !File.Exists(remove) && LibraryCatalog.Usage(root).ActiveBytes == 128);
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
Test("backup restores shared media, session metadata and input events to new paths", () =>
{
    var root = Path.Combine(Path.GetTempPath(), $"echo-backup-{Guid.NewGuid():N}");
    try
    {
        var original = Path.Combine(root, "original");
        var restored = Path.Combine(root, "restored");
        var (match, _) = ManagedLibraryFixture(original);
        LibraryCatalog.Edit(original, [new(match.Id, match.Id, true)], (session, _) =>
        {
            session.Title = "Training recap";
            session.AudioTracks = ["Master", "Game"];
            session.InputLogPath = Path.Combine(LibraryCatalog.Folder(original, match.Id), "inputs.jsonl");
            File.WriteAllLines(session.InputLogPath, [JsonSerializer.Serialize(new InputSample(4, "Q", true), new JsonSerializerOptions(JsonStore.Options) { WriteIndented = false })]);
            session.Moments.Add(new(1, MomentKind.MultiKill, 5, 5) { Streak = 2 });
            return null;
        });
        var archive = Path.Combine(root, "echo.zip");
        LibraryBackup.Save(original, archive, () => false);
        Check(LibraryBackup.Restore(restored, archive, () => false) == 1);
        var item = LibraryCatalog.Read(restored).Single();
        Check(item.Id != match.Id && item.Title == "Training recap" && item.AudioTracks.Length == 2 && item.Moments[0].Label == "Double Kill");
        Check(File.Exists(item.RecordingPath) && item.Clips.All(c => c.Path == item.RecordingPath) && GameplayInputs.Read(item.InputLogPath).Single().Control == "Q");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
Test("malicious backup asset paths are rejected without writing outside staging", () =>
{
    var root = Path.Combine(Path.GetTempPath(), $"echo-backup-unsafe-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(root);
        var archive = Path.Combine(root, "unsafe.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var manifest = zip.CreateEntry("echo-library.json").Open();
            JsonSerializer.Serialize(manifest, new { Version = 1, Sessions = new[] { new Session { EndedAt = DateTimeOffset.UtcNow, RecordingPath = "../escape.mp4" } } }, JsonStore.Options);
        }
        bool refused = false;
        try { LibraryBackup.Restore(Path.Combine(root, "restored"), archive, () => false); }
        catch (InvalidDataException) { refused = true; }
        Check(refused && !File.Exists(Path.Combine(root, "escape.mp4")) && !Directory.EnumerateFileSystemEntries(Path.Combine(root, "restored")).Any());
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
Test("relinking one missing video updates all virtual clips without altering their times", () =>
{
    var root = Path.Combine(Path.GetTempPath(), $"echo-relink-{Guid.NewGuid():N}");
    try
    {
        var (match, missing) = ManagedLibraryFixture(root);
        var replacement = Path.Combine(root, "replacement.mp4");
        File.Move(missing, replacement);
        LibraryCatalog.Relink(root, new(match.Id, "one", false), replacement, 20, () => false);
        var session = LibraryCatalog.Read(root).Single();
        Check(File.Exists(session.RecordingPath) && session.Clips.All(c => c.Path == session.RecordingPath) &&
            session.Clips.Select(c => c.Start).SequenceEqual([1d, 3d]) && File.Exists(replacement));
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});

(Session, string) ManagedLibraryFixture(string root)
{
    var match = new Session { Mode = RecordingMode.FullMatch, Duration = 20, EndedAt = DateTimeOffset.UtcNow };
    var folder = LibraryCatalog.Folder(root, match.Id);
    Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, "match.mp4");
    File.WriteAllBytes(path, new byte[128]);
    match.RecordingPath = path;
    match.Clips.Add(new("one", path, 1, 2, "Kill", true));
    match.Clips.Add(new("two", path, 3, 4, "Kill", true));
    JsonStore.Write(Path.Combine(folder, "session.json"), match);
    return (match, path);
}

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
