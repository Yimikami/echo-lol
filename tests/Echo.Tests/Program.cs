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
Test("force flush clamps unavailable tail", () => { var p = new ReplayScheduler(Settings()); p.Add(new(1, MomentKind.Kill, 20, 20)); Check(p.TakeDue(22, true)?.End == 22); Check(p.Pending.Count == 0); });
Test("long fight is split before buffer eviction", () => { var p = new ReplayScheduler(Settings()); for (int i = 20; i <= 45; i += 5) p.Add(new(i, MomentKind.Kill, i, i)); var part = p.TakeDue(42); Check(part != null && part.Start == 5 && part.End == 42); Check(p.Pending.Single().Start == 41); Check(p.TakeDue(53) != null); });
Test("atomic session roundtrip", () => { var folder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "echo-tests-" + Guid.NewGuid().ToString("N")); try { var path = Path.Combine(folder, "session.json"); var session = new Session(); JsonStore.Write(path, session); Check(JsonStore.Read<Session>(path)?.Id == session.Id); Check(!File.Exists(path + ".tmp")); } finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); } });
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
Test("virtual clip deletion preserves shared match", () => LibraryFixture((root, media, metadata, s) =>
{
    var recycled = LibraryDeletion.Remove(root, s.Id, "one", false, _ => throw new Exception("Must not recycle a shared recording."));
    var result = JsonStore.Read<Session>(metadata)!; Check(!recycled && File.Exists(media) && result.Clips.Single().Id == "two" && result.RecordingPath == media);
}));
Test("match deletion removes dependent virtual clips", () => LibraryFixture((root, media, metadata, s) =>
{
    LibraryDeletion.Remove(root, s.Id, s.Id, true, path => File.Move(path, path + ".recycled"));
    var result = JsonStore.Read<Session>(metadata)!; Check(!File.Exists(media) && File.Exists(media + ".recycled") && result.RecordingPath == null && result.Clips.Count == 0);
}));
Test("failed recycling restores the catalog", () => LibraryFixture((root, media, metadata, s) =>
{
    bool failedRecycle = false;
    try { LibraryDeletion.Remove(root, s.Id, s.Id, true, _ => throw new IOException("fixture recycling failed")); } catch (IOException) { failedRecycle = true; }
    var result = JsonStore.Read<Session>(metadata)!; Check(failedRecycle && File.Exists(media) && result.Clips.Count == 2 && result.RecordingPath == media);
}));
Test("active recording cannot be deleted", () => LibraryFixture((root, media, metadata, s) =>
{
    s.EndedAt = null; JsonStore.Write(metadata, s); bool blocked = false;
    try { LibraryDeletion.Remove(root, s.Id, s.Id, true, _ => throw new Exception("Must not recycle.")); } catch (InvalidOperationException) { blocked = true; }
    Check(blocked && File.Exists(media));
}));
Test("preparing media cannot race library deletion", () => LibraryFixture((root, media, metadata, s) =>
{
    s.PreparationPending = true; JsonStore.Write(metadata, s); bool blocked = false;
    try { LibraryDeletion.Remove(root, s.Id, s.Id, true, _ => throw new Exception("Must not recycle.")); } catch (InvalidOperationException) { blocked = true; }
    Check(blocked && File.Exists(media) && JsonStore.Read<Session>(metadata)!.Clips.Count == 2);
}));
Test("standalone clip deletion recycles its file", () => LibraryFixture((root, media, metadata, s) =>
{
    s.RecordingPath = null; s.Clips.Clear(); s.Clips.Add(new("standalone", media, 0, 10, "Kill", false)); JsonStore.Write(metadata, s);
    LibraryDeletion.Remove(root, s.Id, "standalone", false, path => File.Move(path, path + ".recycled"));
    Check(!File.Exists(media) && JsonStore.Read<Session>(metadata)!.Clips.Count == 0);
}));
Test("deletion rejects media outside its session", () => LibraryFixture((root, media, metadata, s) =>
{
    s.RecordingPath = Path.Combine(root, "unrelated.mp4"); JsonStore.Write(metadata, s); bool blocked = false;
    try { LibraryDeletion.Remove(root, s.Id, s.Id, true, _ => throw new Exception("Must not recycle.")); } catch (InvalidOperationException) { blocked = true; }
    Check(blocked && File.Exists(media) && JsonStore.Read<Session>(metadata)!.Clips.Count == 2);
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
Test("overlay presets reject invalid sizes and positions",()=>{Throws(()=>new InputOverlayPreset{Width=.01}.Validate());Throws(()=>new InputOverlayPreset{X=double.NaN}.Validate());});
Test("overlay presets reject unknown layouts",()=>Throws(()=>new InputOverlayPreset{Layout="unknown"}.Validate()));
Test("all overlay layouts have visible keys within their bounds",()=>{foreach(var name in new[]{"Compact","Full","Mouse"}){var layout=OverlayAppearance.Layout(name);Check(layout.Keys.Count>0&&layout.Keys.All(k=>k.X>=0&&k.Y>=0&&k.X+k.Width<=layout.Width&&k.Y+k.Height<=layout.Height));}});
Test("legacy settings receive an overlay preset",()=>{var s=JsonSerializer.Deserialize<EchoSettings>("{}",JsonStore.Options)!;Check(s.InputOverlay.Enabled&&s.InputOverlay.Layout=="Compact"&&s.Theme=="Dark");});
Test("full keyboard F keys and navigation are permitted controls",()=>Check(GameplayInputs.Allowed.Contains("F12")&&GameplayInputs.Allowed.Contains("Left")&&GameplayInputs.Allowed.Contains("Z")));
Test("theme selection is validated",()=>Throws(()=>(Settings() with{Theme="unknown"}).Validate()));
Test("exit request is durable and carries a new identity",()=>{var root=Path.Combine(Path.GetTempPath(),"echo-exit-"+Guid.NewGuid());try{var p=new HostPaths(root);Check(ApplicationExit.Current(p)==null);ApplicationExit.Request(p);var first=ApplicationExit.Current(p);Check(first!=null&&File.Exists(p.StopRequest));ApplicationExit.Request(p);Check(ApplicationExit.Current(p)!=first);}finally{Directory.Delete(root,true);}});
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
