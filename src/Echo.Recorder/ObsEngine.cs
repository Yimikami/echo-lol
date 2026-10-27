using System.Diagnostics;
using System.Runtime.InteropServices;
using Echo.Core;

namespace Echo.Recorder;

internal sealed class ObsEngine : IDisposable
{
    private nint _video, _audio, _output, _scene, _gameAudio, _capture;
    private readonly List<nint> _sources = [];
    private bool _started, _com;
    private Native.RenderCallback? _focusGuard;
    private static bool _resolverInstalled;
    public string Version { get; private set; } = "";
    public string Encoder { get; private set; } = "";
    public IReadOnlyList<string> Encoders { get; private set; } = [];
    public IReadOnlyList<string> Inputs { get; private set; } = [];
    public int Frames => _output != 0 ? Native.obs_output_get_total_frames(_output) : 0;
    public int DroppedFrames => _output != 0 ? Native.obs_output_get_frames_dropped(_output) : 0;
    public uint RenderingLagFrames => _started ? Native.obs_get_lagged_frames() : 0;
    public uint EncodingLagFrames => _started ? Native.video_output_get_skipped_frames(Native.obs_get_video()) : 0;
    public uint RenderedFrames => _started ? Native.obs_get_total_frames() : 0;
    public bool Active => _output != 0 && Native.obs_output_active(_output);
    public bool HasVideo => _capture != 0 && Native.obs_source_get_width(_capture) > 0 && Native.obs_source_get_height(_capture) > 0;
    public string? LastError => _output != 0 ? Native.Text(Native.obs_output_get_last_error(_output)) : null;

    public void Initialize(EchoSettings settings, string configurationDirectory)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "runtime");
        var obsRoot = settings.ObsDirectory ?? Environment.GetEnvironmentVariable("ECHO_OBS_DIR") ?? (Directory.Exists(bundled) ? bundled : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "obs-studio"));
        var bin = Path.Combine(obsRoot, "bin", "64bit");
        if (!File.Exists(Path.Combine(bin, "obs.dll"))) throw new FileNotFoundException("OBS runtime not found. Run scripts/setup-runtime.ps1, or set obsDirectory in settings.json.");
        Native.SetDllDirectoryW(bin);
        Environment.SetEnvironmentVariable("PATH", bin + ";" + Environment.GetEnvironmentVariable("PATH"));
        if (!_resolverInstalled)
        {
            NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (name, _, _) => name == "obs" ? NativeLibrary.Load(Path.Combine(bin, "obs.dll")) : 0);
            _resolverInstalled = true;
        }
        _com = Native.CoInitializeEx(0, 0) >= 0;
        Directory.CreateDirectory(configurationDirectory);
        if (!Native.obs_startup("en-US", configurationDirectory, 0)) throw new InvalidOperationException("OBS core could not initialize.");
        _started = true;
        Version = Native.Text(Native.obs_get_version_string());
        if (!Version.StartsWith("33.", StringComparison.Ordinal)) throw new InvalidOperationException($"OBS {Version} is not validated. Echo currently targets OBS 33.x.");
        Native.obs_add_data_path(Path.Combine(obsRoot, "data", "libobs").Replace('\\', '/') + "/");
        ConfigureVideo(settings);
        var audioInfo = new Native.AudioInfo { Samples = 48000, Speakers = 2 };
        if (!Native.obs_reset_audio(ref audioInfo)) throw new InvalidOperationException("OBS audio initialization failed.");
        foreach (var name in new[] { "win-capture", "win-wasapi", "obs-ffmpeg", "obs-nvenc", "obs-qsv11", "image-source", "coreaudio-encoder" })
        {
            var modern = Path.Combine(obsRoot, "core", name);
            var dll = Directory.Exists(modern) ? Path.Combine(modern, name + ".dll") : Path.Combine(obsRoot, "obs-plugins", "64bit", name + ".dll");
            var data = Directory.Exists(modern) ? Path.Combine(modern, "data") : Path.Combine(obsRoot, "data", "obs-plugins", name);
            if (!File.Exists(dll)) continue;
            if (Native.obs_open_module(out var module, dll.Replace('\\', '/'), data.Replace('\\', '/')) == 0 && !Native.obs_init_module(module)) Console.Error.WriteLine($"Module unavailable: {name}");
        }
        Native.obs_post_load_modules();
        List<string> encoders = [], inputs = [];
        for (nuint i = 0; Native.obs_enum_encoder_types(i, out var id); i++) encoders.Add(Native.Text(id));
        for (nuint i = 0; Native.obs_enum_input_types(i, out var id); i++) inputs.Add(Native.Text(id));
        Encoders = encoders; Inputs = inputs;
        Console.WriteLine($"Echo native runtime {Version}; encoders: {string.Join(", ", Encoders)}");
    }

    public void Start(EchoSettings settings, CaptureWindow? target, string sessionDirectory, bool synthetic = false)
    {
        settings.Validate();
        _scene = Native.obs_scene_create("Echo gameplay");
        StartSources(settings, target, sessionDirectory, synthetic);
    }

    private static void ConfigureVideo(EchoSettings settings)
    {
        var module = Marshal.StringToCoTaskMemUTF8("libobs-d3d11");
        try
        {
            var video = new Native.VideoInfo { GraphicsModule = module, FpsNum = (uint)settings.Fps, FpsDen = 1,
                BaseWidth = (uint)settings.Width, BaseHeight = (uint)settings.Height, OutputWidth = (uint)settings.Width,
                OutputHeight = (uint)settings.Height, Format = 2, GpuConversion = 1, Colorspace = 2, Range = 1, ScaleType = 2 };
            var code = Native.obs_reset_video(ref video);
            if (code != 0) throw new InvalidOperationException($"OBS video initialization failed ({code}).");
        }
        finally { Marshal.FreeCoTaskMem(module); }
    }

    private void StartSources(EchoSettings settings, CaptureWindow? target, string sessionDirectory, bool synthetic)
    {
        using var capture = new ObsData();
        string sourceId;
        if (synthetic)
        {
            sourceId = Inputs.FirstOrDefault(id => id.StartsWith("color_source", StringComparison.Ordinal)) ?? throw new InvalidOperationException("Test color source unavailable.");
            capture.Set("color", 0xffb8d870).Set("width", settings.Width).Set("height", settings.Height);
        }
        else
        {
            if (target == null || Windows.IsIconic(target.Handle)) throw new InvalidOperationException("The game window must be visible and not minimized. Use borderless mode.");
            if (settings.CaptureMethod == CaptureMethod.FullscreenDisplay)
            {
                sourceId = "monitor_capture";
                capture.Set("monitor_id", Windows.MonitorId(target)).Set("method", 1).Set("capture_cursor", true);
            }
            else
            {
                sourceId = "window_capture";
                capture.Set("window", target.Selector).Set("method", 2).Set("priority", 2).Set("cursor", true).Set("client_area", true).Set("capture_audio", false);
            }
        }
        var source = CreateSource(sourceId, "Echo video", capture); _capture = source;
        var item = Native.obs_scene_add(_scene, source);
        if (!synthetic && target != null && settings.CaptureMethod == CaptureMethod.FullscreenDisplay)
        {
            // Evaluated on the OBS rendering thread immediately before composing each frame.
            // No pixel buffers or per-frame allocations enter managed code.
            var processId = target.ProcessId;
            var monitorHandle = Windows.MonitorHandle(target.Handle);
            bool lastVisible = false;
            Native.obs_sceneitem_set_visible(item, false);
            _focusGuard = (_, _, _) =>
            {
                var visible = Windows.IsForeground(processId) && Windows.FillsMonitor(target.Handle, monitorHandle);
                if (visible != lastVisible) { Native.obs_sceneitem_set_visible(item, visible); lastVisible = visible; }
            };
            Native.obs_add_main_render_callback(_focusGuard, 0);
        }
        Native.obs_sceneitem_set_bounds_type(item, 2); // OBS_BOUNDS_SCALE_INNER, retain aspect ratio
        var bounds = new Native.Vector2 { X = settings.Width, Y = settings.Height };
        Native.obs_sceneitem_set_bounds(item, ref bounds);
        Native.obs_set_output_source(0, Native.obs_scene_get_source(_scene));

        uint channel = 1;
        if (settings.GameAudio && !settings.DesktopAudio && !synthetic && target != null)
        {
            if (!Inputs.Contains("wasapi_process_output_capture")) throw new InvalidOperationException("Application audio is unavailable on this Windows/runtime version. Desktop audio will not be substituted.");
            using var data = new ObsData(); data.Set("window", target.Selector).Set("priority", 2);
            _gameAudio = CreateSource("wasapi_process_output_capture", "League audio", data);
            Native.obs_set_output_source(channel++, _gameAudio);
        }
        if (settings.DesktopAudio && !synthetic)
        {
            using var data = new ObsData(); data.Set("device_id", "default");
            Native.obs_set_output_source(channel++, CreateSource("wasapi_output_capture", "Desktop audio (opt-in)", data));
        }
        if (settings.Microphone && !synthetic)
        {
            using var data = new ObsData(); data.Set("device_id", "default");
            Native.obs_set_output_source(channel, CreateSource("wasapi_input_capture", "Microphone (opt-in)", data));
        }
        var choices = settings.Encoder switch
        {
            "nvenc" => new[] { "obs_nvenc_h264_tex", "jim_nvenc", "ffmpeg_nvenc" },
            "amf" => new[] { "h264_texture_amf", "amd_amf_h264" },
            "qsv" => new[] { "obs_qsv11_v2", "obs_qsv11" },
            _ => new[] { "obs_nvenc_h264_tex", "jim_nvenc", "h264_texture_amf", "amd_amf_h264", "obs_qsv11_v2", "obs_qsv11", "ffmpeg_nvenc" }
        };
        Encoder = choices.FirstOrDefault(Encoders.Contains) ?? throw new InvalidOperationException("No supported hardware H.264 encoder is available. CPU fallback is disabled.");
        using var videoSettings = new ObsData();
        videoSettings.Set("rate_control", "CBR").Set("bitrate", settings.BitrateKbps).Set("keyint_sec", 1)
            .Set("preset", "p3").Set("profile", "high").Set("multipass", "disabled").Set("lookahead", false).Set("psycho_aq", false).Set("adaptive_quantization", false).Set("bf", 0);
        _video = Native.obs_video_encoder_create(Encoder, "Echo hardware H.264", videoSettings.Handle, 0);
        if (_video == 0) throw new InvalidOperationException($"Could not create {Encoder}.");
        Native.obs_encoder_set_video(_video, Native.obs_get_video());
        var audioId = new[] { "ffmpeg_aac", "CoreAudio_AAC" }.FirstOrDefault(Encoders.Contains) ?? throw new InvalidOperationException("AAC encoder unavailable.");
        using var audioSettings = new ObsData(); audioSettings.Set("bitrate", 192);
        _audio = Native.obs_audio_encoder_create(audioId, "Echo AAC", audioSettings.Handle, 0, 0);
        if (_audio == 0) throw new InvalidOperationException("Could not create AAC encoder.");
        Native.obs_encoder_set_audio(_audio, Native.obs_get_audio());
        Directory.CreateDirectory(sessionDirectory);
        using var outputSettings = new ObsData();
        bool replay = settings.Mode == RecordingMode.Clips;
        if (replay)
            outputSettings.Set("directory", sessionDirectory).Set("format", "Echo-%CCYY-%MM-%DD-%hh-%mm-%ss").Set("extension", "mkv")
                .Set("max_time_sec", settings.BufferSeconds).Set("max_size_mb", settings.BufferMegabytes).Set("allow_spaces", false);
        else outputSettings.Set("path", Path.Combine(sessionDirectory, "match.mkv"));
        _output = Native.obs_output_create(replay ? "replay_buffer" : "ffmpeg_muxer", "Echo output", outputSettings.Handle, 0);
        if (_output == 0) throw new InvalidOperationException("Could not create recording output.");
        Native.obs_output_set_video_encoder(_output, _video); Native.obs_output_set_audio_encoder(_output, _audio, 0);
        if (!Native.obs_output_start(_output)) throw new InvalidOperationException($"Recording failed: {LastError}");
    }

    private nint CreateSource(string id, string name, ObsData settings)
    {
        var source = Native.obs_source_create(id, name, settings.Handle, 0);
        if (source == 0) throw new InvalidOperationException($"Could not create {id}.");
        _sources.Add(source); return source;
    }
    public bool? AudioHooked()
    {
        if (_gameAudio == 0) return null;
        Native.CallData data = default;
        try
        {
            if (!Native.proc_handler_call(Native.obs_source_get_proc_handler(_gameAudio), "get_hooked", ref data)) return false;
            return Native.calldata_get_data(ref data, "hooked", out var hooked, 1) && hooked != 0;
        }
        finally { Native.Free(ref data); }
    }
    public void SaveReplay()
    {
        Native.CallData data = default;
        try
        {
            if (!Native.proc_handler_call(Native.obs_output_get_proc_handler(_output), "save", ref data)) throw new InvalidOperationException("Replay save was rejected.");
        }
        finally { Native.Free(ref data); }
    }
    public string? LastReplay()
    {
        Native.CallData data = default;
        try
        {
            if (!Native.proc_handler_call(Native.obs_output_get_proc_handler(_output), "get_last_replay", ref data)) return null;
            return Native.calldata_get_string(ref data, "path", out var ptr) ? Native.Text(ptr) : null;
        }
        finally { Native.Free(ref data); }
    }
    public void Stop()
    {
        if (_output == 0 || !Native.obs_output_active(_output)) return;
        Native.obs_output_stop(_output);
        var wait = Stopwatch.StartNew();
        while (Native.obs_output_active(_output) && wait.Elapsed.TotalSeconds < 10) Thread.Sleep(50);
        if (Native.obs_output_active(_output)) Native.obs_output_force_stop(_output);
    }
    public void Dispose()
    {
        if (!_started) { if (_com) { Native.CoUninitialize(); _com = false; } return; }
        Stop();
        if (_focusGuard != null) { Native.obs_remove_main_render_callback(_focusGuard, 0); _focusGuard = null; }
        if (_output != 0) Native.obs_output_release(_output);
        for (uint i = 0; i < 6; i++) Native.obs_set_output_source(i, 0);
        if (_scene != 0) Native.obs_scene_release(_scene);
        foreach (var source in _sources) Native.obs_source_release(source);
        if (_video != 0) Native.obs_encoder_release(_video);
        if (_audio != 0) Native.obs_encoder_release(_audio);
        Native.obs_shutdown(); _started = false; _output = 0;
        if (_com) { Native.CoUninitialize(); _com = false; }
    }
}
