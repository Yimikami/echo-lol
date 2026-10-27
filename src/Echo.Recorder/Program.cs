using System.Diagnostics;
using System.Text.Json;
using Echo.Core;

namespace Echo.Recorder;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? Arg(string key) { var i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var paths = new HostPaths(Arg("--data-dir"));
        if (args.Contains("--fixture")) { ApplicationConfiguration.Initialize(); Application.Run(new CaptureFixture(int.Parse(Arg("--tone") ?? "440"))); return 0; }
        if (args.Contains("--help"))
        {
            Console.WriteLine("Echo.Recorder [--data-dir PATH] [--diagnose | --record --seconds 20 [--synthetic | --window TITLE] [--mode clips|full]]\nNo arguments: watch for League, with tray controls. Never captures the desktop implicitly."); return 0;
        }
        using var mutex = new Mutex(true, "Local\\" + paths.PipeName, out var first);
        if (!first) { Console.Error.WriteLine("Echo's recorder is already running for this profile."); return 2; }
        try
        {
            if (!File.Exists(paths.Settings)) JsonStore.Write(paths.Settings, new EchoSettings());
            if (args.Contains("--diagnose"))
            {
                using var engine = new ObsEngine(); engine.Initialize(paths.LoadSettings(), Path.Combine(paths.Root, "native"));
                JsonStore.Write(Path.Combine(paths.Root, "diagnostics.json"), new { engine.Version, engine.Encoders, engine.Inputs, VideoInfoSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.VideoInfo>() });
                Console.WriteLine("Diagnostics saved."); return 0;
            }
            if (args.Contains("--record")) return RecordTest(paths, args);
            using var cancellation = new CancellationTokenSource();
            var host = new RecorderHost(paths, cancellation.Token);
            var pipe = host.ServeAsync();
            var worker = new Thread(host.Watch) { IsBackground = true, Name = "Echo recorder" }; worker.SetApartmentState(ApartmentState.MTA); worker.Start();
            ApplicationConfiguration.Initialize();
            using var tray = new NotifyIcon { Text = "Echo — waiting for League", Icon = System.Drawing.SystemIcons.Application, Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Echo", null, (_, _) => OpenUi(paths));
            menu.Items.Add("Save moment", null, (_, _) => host.Enqueue("save"));
            menu.Items.Add("Resume capture", null, (_, _) => host.Enqueue("arm"));
            menu.Items.Add("Pause capture", null, (_, _) => host.Enqueue("disarm"));
            menu.Items.Add("Exit recorder", null, (_, _) => { cancellation.Cancel(); Application.ExitThread(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => OpenUi(paths);
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); Application.Exit(); };
            Application.Run();
            cancellation.Cancel(); worker.Join(TimeSpan.FromSeconds(30));
            tray.Visible = false;
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); JsonStore.Write(paths.Status, new RecorderStatus { State = "Recorder needs attention", Error = e.Message }); return 1; }
    }

    private static int RecordTest(HostPaths paths, string[] args)
    {
        string? Arg(string key) { var i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var seconds = int.Parse(Arg("--seconds") ?? "20");
        if (seconds is < 3 or > 3600) throw new ArgumentException("Test duration must be 3–3600 seconds.");
        bool synthetic = args.Contains("--synthetic");
        var target = synthetic ? null : Windows.Find(Arg("--window"));
        if (!synthetic && target == null) throw new InvalidOperationException("The selected test window was not found.");
        var settings = paths.LoadSettings() with { Mode = Arg("--mode") == "clips" ? RecordingMode.Clips : RecordingMode.FullMatch,
            BeforeSeconds = 3, AfterSeconds = 2, BufferSeconds = 12, BufferMegabytes = 64 };
        var session = new Session { Mode = settings.Mode, Champion = synthetic ? "Synthetic capture test" : "Window capture test" };
        var folder = Path.Combine(settings.OutputDirectory, session.Id); Directory.CreateDirectory(folder);
        var planner = new MomentPlanner(settings); var scheduler = new ReplayScheduler(settings);
        using var engine = new ObsEngine(); engine.Initialize(settings, Path.Combine(paths.Root, "native"));
        engine.Start(settings, target, folder, synthetic);
        if (settings.Mode == RecordingMode.FullMatch) session.RecordingPath = Path.Combine(folder, "match.mkv");
        var clock = Stopwatch.StartNew(); using var process = Process.GetCurrentProcess();
        double cpuStart = process.TotalProcessorTime.TotalSeconds;
        var samples = new List<object>(); double nextSample = 1, nextEvent = 4;
        var detector = new EventDetector();
        using var initial = JsonDocument.Parse("{\"activePlayer\":{\"riotId\":\"Echo#TEST\",\"riotIdGameName\":\"Echo\"},\"Events\":[]}");
        detector.Identify(initial.RootElement); detector.Consume(initial.RootElement, 0, 0, true);
        var events = new List<object>();
        try
        {
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                var now = clock.Elapsed.TotalSeconds;
                if (!engine.Active) throw new InvalidOperationException(engine.LastError ?? "Encoder stopped.");
                if (now >= nextEvent)
                {
                    var id = events.Count + 1;
                    var kind = id % 3;
                    events.Add(new { EventID = id, EventName = "ChampionKill", EventTime = now, KillerName = kind == 1 ? "Echo" : "Other", VictimName = kind == 0 ? "Echo" : "Opponent", Assisters = kind == 2 ? new[] { "Echo" } : Array.Empty<string>() });
                    using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { Events = events }));
                    foreach (var m in detector.Consume(document.RootElement, now, now)) { session.Moments.Add(m); planner.Add(m); scheduler.Add(m); }
                    nextEvent += 6;
                }
                if (settings.Mode == RecordingMode.Clips && scheduler.TakeDue(now) is { } due) RecorderHost.SaveReplay(engine, session, due, clock, settings);
                if (now >= nextSample)
                {
                    process.Refresh();
                    samples.Add(new { Seconds = now, WorkingSetMB = process.WorkingSet64 / 1048576d, PrivateMB = process.PrivateMemorySize64 / 1048576d, engine.Frames, engine.DroppedFrames, GameAudioHooked = engine.AudioHooked() });
                    nextSample = now + 1;
                }
                Thread.Sleep(50);
            }
            if (settings.Mode == RecordingMode.Clips)
                while (scheduler.TakeDue(clock.Elapsed.TotalSeconds, true) is { } due) RecorderHost.SaveReplay(engine, session, due, clock, settings);
        }
        finally { engine.Stop(); }
        session.Duration = clock.Elapsed.TotalSeconds; session.EndedAt = DateTimeOffset.UtcNow; session.State = "Complete";
        var cpuEnd = process.TotalProcessorTime.TotalSeconds;
        JsonStore.Write(Path.Combine(folder, "measurements.json"), new { Kind = synthetic ? "synthetic, not gameplay" : "window fixture, not gameplay", engine.Version, engine.Encoder,
            Duration = session.Duration, CpuPercent = (cpuEnd - cpuStart) / session.Duration / Environment.ProcessorCount * 100, engine.Frames, engine.DroppedFrames, Samples = samples });
        if (session.RecordingPath is { } source)
        {
            session.RecordingPath = MediaTools.Remux(source); File.Delete(source);
            foreach (var range in planner.Ranges)
                session.Clips.Add(new(Guid.NewGuid().ToString("N"), session.RecordingPath, range.Start, Math.Min(range.End, session.Duration), range.Label, true));
        }
        foreach (var clip in session.Clips.ToArray())
        {
            if (!clip.Path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)) continue;
            var mp4 = MediaTools.Remux(clip.Path); session.Clips[session.Clips.IndexOf(clip)] = clip with { Path = mp4 }; File.Delete(clip.Path);
        }
        foreach (var clip in session.Clips) MediaTools.Thumbnail(clip.Path, Path.Combine(folder, clip.Id + ".jpg"), clip.Start);
        JsonStore.Write(Path.Combine(folder, "session.json"), session);
        Console.WriteLine($"Echo test saved to {folder}");
        return 0;
    }
    private static void OpenUi(HostPaths paths)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Echo.exe");
        if (!File.Exists(file)) { MessageBox.Show("Launch Echo from its application folder.", "Echo"); return; }
        var start = new ProcessStartInfo(file) { UseShellExecute = false }; start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(paths.Root); Process.Start(start);
    }
}
