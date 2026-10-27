using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Echo.Core;

namespace Echo.Recorder;

internal sealed class RecorderHost(HostPaths paths, CancellationToken cancellation)
{
    private readonly ConcurrentQueue<string> _commands = new();
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private double _lastWall, _lastCpu, _cpu;
    private bool _armed = true;
    private RecorderStatus _status = new();
    public void Enqueue(string command) => _commands.Enqueue(command);

    public async Task ServeAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(paths.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellation);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(2000);
                using var reader = new StreamReader(server, leaveOpen: true);
                var command = await reader.ReadLineAsync(timeout.Token);
                if (command is "arm" or "disarm" or "save" or "retry") _commands.Enqueue(command);
                using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(_status, JsonStore.Options).AsMemory(), timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
            catch (IOException) { }
            catch (OperationCanceledException) { break; }
        }
    }
    public void Watch()
    {
        using var league = new LeagueClient();
        int failedProcess = -1;
        while (!cancellation.IsCancellationRequested)
        {
            while (_commands.TryDequeue(out var c))
            {
                if (c == "disarm") _armed = false;
                if (c is "arm" or "retry") { _armed = true; failedProcess = -1; }
            }
            var target = _armed ? Windows.Find() : null;
            if (target == null) failedProcess = -1;
            if (target == null || Windows.IsIconic(target.Handle) || target.ProcessId == failedProcess)
            {
                Publish(_status with { Armed = _armed, Recording = false, State = !_armed ? "Capture paused" : failedProcess >= 0 ? "Recording needs attention" : "Waiting for League" });
                cancellation.WaitHandle.WaitOne(1500); continue;
            }
            try
            {
                var settings = paths.LoadSettings();
                using var initial = league.GetAsync("allgamedata", cancellation).GetAwaiter().GetResult();
                if (!initial.RootElement.TryGetProperty("gameData", out _)) { cancellation.WaitHandle.WaitOne(1000); continue; }
                Record(settings, target, league, initial.RootElement);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                if (e is HttpRequestException or TaskCanceledException) { cancellation.WaitHandle.WaitOne(1500); continue; }
                failedProcess = target.ProcessId;
                Publish(_status with { Armed = _armed, Recording = false, State = "Recording needs attention", Error = e.Message });
                Console.Error.WriteLine(e);
            }
        }
        Publish(_status with { Armed = false, Recording = false, State = "Recorder stopped" });
    }

    private void Record(EchoSettings settings, CaptureWindow target, LeagueClient league, JsonElement initial)
    {
        var session = new Session { Mode = settings.Mode };
        var folder = Path.Combine(settings.OutputDirectory, session.Id);
        Directory.CreateDirectory(folder);
        var metadata = Path.Combine(folder, "session.json");
        JsonStore.Write(metadata, session);
        var timeline = new MomentPlanner(settings); var pending = new ReplayScheduler(settings); var detector = new EventDetector();
        detector.Identify(initial);
        var initialTime = initial.GetProperty("gameData").GetProperty("gameTime").GetDouble();
        detector.Consume(initial, initialTime, 0, baseline: true);
        using var engine = new ObsEngine();
        var clock = new Stopwatch();
        try
        {
            CheckDisk(settings);
            engine.Initialize(settings, Path.Combine(paths.Root, "native"));
            engine.Start(settings, target, folder);
            clock.Start();
            if (settings.Mode == RecordingMode.FullMatch) session.RecordingPath = Path.Combine(folder, "match.mkv");
            double nextPoll = 0, nextStatus = 0, nextDisk = 0, gameTime = initialTime;
            int disconnected = 0, manualId = -1;
            while (!cancellation.IsCancellationRequested && _armed)
            {
                var now = clock.Elapsed.TotalSeconds;
                while (_commands.TryDequeue(out var cmd))
                {
                    if (cmd == "disarm") _armed = false;
                    if (cmd == "save") Add(new(manualId--, MomentKind.Manual, gameTime, now));
                }
                if (!_armed) break;
                if (!engine.Active) throw new InvalidOperationException($"Recording stopped unexpectedly: {engine.LastError}");
                if (now >= nextPoll)
                {
                    nextPoll = now + .5;
                    try
                    {
                        using var stats = league.GetAsync("gamestats", cancellation).GetAwaiter().GetResult();
                        using var events = league.GetAsync("eventdata", cancellation).GetAwaiter().GetResult();
                        gameTime = stats.RootElement.GetProperty("gameTime").GetDouble();
                        // The response is mapped to the clock sample taken with gamestats, not after event retrieval.
                        var sampleCapture = clock.Elapsed.TotalSeconds;
                        foreach (var moment in detector.Consume(events.RootElement, gameTime, sampleCapture)) Add(moment);
                        disconnected = 0;
                    }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
                    {
                        disconnected++;
                        // A short API outage does not terminate video capture. A closed process does.
                        if (disconnected >= 3 && !ProcessExists(target.ProcessId)) break;
                        if (disconnected > 20) throw new InvalidOperationException("League event data has been unavailable for too long. Recording was finalized; retry when the API returns.", e);
                    }
                    if (!ProcessExists(target.ProcessId)) break;
                }
                if (settings.Mode == RecordingMode.Clips)
                {
                    var due = pending.TakeDue(clock.Elapsed.TotalSeconds);
                    if (due != null) SaveReplay(engine, session, due, clock, settings);
                }
                if (now >= nextDisk) { CheckDisk(settings); nextDisk = now + 5; }
                if (now >= nextStatus)
                {
                    var hooked = engine.AudioHooked();
                    Publish(new() { Armed = _armed, Recording = true, State = Windows.IsIconic(target.Handle) ? "Game minimized — video may pause" : "Recording", Encoder = engine.Encoder,
                        NativeVersion = engine.Version, ElapsedSeconds = now, Frames = engine.Frames, DroppedFrames = engine.DroppedFrames,
                        SessionId = session.Id, GameAudioHooked = hooked,
                        Error = settings.GameAudio && now > 10 && hooked == false ? "Game audio has not attached. Desktop audio remains off unless enabled." : null });
                    session.Duration = now; JsonStore.Write(metadata, session); nextStatus = now + 1;
                }
                cancellation.WaitHandle.WaitOne(100);
            }
            if (settings.Mode == RecordingMode.Clips)
                while (pending.TakeDue(clock.Elapsed.TotalSeconds, force: true) is { } remaining) SaveReplay(engine, session, remaining, clock, settings);
            session.State = "Complete";
        }
        catch (Exception e)
        {
            session.State = "Interrupted"; session.Error = e.Message;
            throw;
        }
        finally
        {
            engine.Stop();
            session.Duration = clock.Elapsed.TotalSeconds; session.EndedAt = DateTimeOffset.UtcNow;
            Publish(_status with { Recording = false, State = "Finalizing recording" });
            try
            {
                if (session.RecordingPath is { } source && File.Exists(source))
                {
                    var mp4 = MediaTools.Remux(source); session.RecordingPath = mp4;
                    foreach (var range in timeline.Ranges)
                        session.Clips.Add(new(Guid.NewGuid().ToString("N"), mp4, range.Start, Math.Min(range.End, session.Duration), range.Label, true));
                    File.Delete(source); // Only our own finalized intermediate; validated MP4 is retained.
                }
                foreach (var clip in session.Clips.ToArray())
                {
                    var clipSource = clip.Path;
                    if (clipSource.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
                    {
                        var mp4 = MediaTools.Remux(clipSource);
                        var index = session.Clips.IndexOf(clip);
                        session.Clips[index] = clip with { Path = mp4 };
                        File.Delete(clipSource);
                    }
                }
                foreach (var clip in session.Clips)
                    MediaTools.Thumbnail(clip.Path, Path.Combine(folder, clip.Id + ".jpg"), clip.Start + Math.Min(2, (clip.End - clip.Start) / 2));
            }
            catch (Exception e) { session.Error = "Recording retained; media preparation needs attention: " + e.Message; Console.Error.WriteLine(e.Message); }
            JsonStore.Write(metadata, session);
        }
        void Add(Moment moment)
        {
            session.Moments.Add(moment); timeline.Add(moment); pending.Add(moment);
            if (initial.TryGetProperty("allPlayers", out var players) && initial.TryGetProperty("activePlayer", out var active) && active.TryGetProperty("riotId", out var identity))
                foreach (var p in players.EnumerateArray())
                    if (p.TryGetProperty("riotId", out var id) && id.GetString() == identity.GetString() && p.TryGetProperty("championName", out var champion)) session.Champion = champion.GetString() ?? session.Champion;
            JsonStore.Write(metadata, session);
        }
    }

    internal static void SaveReplay(ObsEngine engine, Session session, ClipRange range, Stopwatch clock, EchoSettings settings)
    {
        var previous = engine.LastReplay(); var requestTime = clock.Elapsed.TotalSeconds;
        engine.SaveReplay();
        var timeout = Stopwatch.StartNew(); string? saved = null;
        while (timeout.Elapsed.TotalSeconds < 10)
        {
            Thread.Sleep(100);
            var candidate = engine.LastReplay();
            if (!string.IsNullOrEmpty(candidate) && candidate != previous && File.Exists(candidate)) { saved = candidate; break; }
        }
        if (saved == null) throw new IOException("The replay save did not complete. The recording session was retained.");
        var duration = MediaTools.Duration(saved);
        var origin = requestTime - duration;
        var start = Math.Max(0, range.Start - origin); var end = Math.Min(duration, range.End - origin);
        if (end <= start) throw new InvalidDataException("Replay timestamps could not be mapped to the saved file.");
        session.Clips.Add(new(Guid.NewGuid().ToString("N"), saved, start, end, range.Label.Length == 0 ? "Continued moment" : range.Label, false));
    }
    private static bool ProcessExists(int id) { try { using var p = Process.GetProcessById(id); return !p.HasExited; } catch (ArgumentException) { return false; } }
    private static void CheckDisk(EchoSettings settings)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(settings.OutputDirectory))!);
        if (drive.AvailableFreeSpace < settings.MinimumFreeMegabytes * 1024 * 1024) throw new IOException("Not enough free disk space. Recording was stopped to protect saved clips.");
    }
    private void Publish(RecorderStatus status)
    {
        _process.Refresh(); var wall = _uptime.Elapsed.TotalSeconds; var cpu = _process.TotalProcessorTime.TotalSeconds;
        if (wall > _lastWall) _cpu = (cpu - _lastCpu) / (wall - _lastWall) / Environment.ProcessorCount * 100;
        _lastCpu = cpu; _lastWall = wall;
        _status = status with { CpuPercent = Math.Round(_cpu, 2), WorkingSetMegabytes = Math.Round(_process.WorkingSet64 / 1048576d, 1),
            PrivateMegabytes = Math.Round(_process.PrivateMemorySize64 / 1048576d, 1), UpdatedAt = DateTimeOffset.UtcNow };
        JsonStore.Write(paths.Status, _status);
    }
}
