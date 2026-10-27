using System.Runtime.InteropServices;

namespace Echo.Recorder;

// A deliberately narrow, original binding to OBS Studio's documented C API.
// Keep this ABI aligned with the pinned OBS runtime; no video frames cross it.
internal static partial class Native
{
    private const string Lib = "obs";
    [StructLayout(LayoutKind.Sequential)] internal struct VideoInfo
    {
        public nint GraphicsModule;
        public uint FpsNum, FpsDen, BaseWidth, BaseHeight, OutputWidth, OutputHeight;
        public int Format;
        public uint Adapter;
        public byte GpuConversion;
        public int Colorspace, Range, ScaleType;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct AudioInfo { public uint Samples; public int Speakers; }
    [StructLayout(LayoutKind.Sequential)] internal struct CallData { public nint Stack; public nuint Size, Capacity; public byte Fixed; }

    [LibraryImport("kernel32", StringMarshalling = StringMarshalling.Utf16)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool SetDllDirectoryW(string path);
    [LibraryImport("ole32")] internal static partial int CoInitializeEx(nint reserved, uint flags);
    [LibraryImport("ole32")] internal static partial void CoUninitialize();
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] [return: MarshalAs(UnmanagedType.I1)] internal static partial bool obs_startup(string locale, string config, nint profiler);
    [LibraryImport(Lib)] internal static partial void obs_shutdown();
    [LibraryImport(Lib)] internal static partial nint obs_get_version_string();
    [LibraryImport(Lib)] internal static partial int obs_reset_video(ref VideoInfo info);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] internal static partial bool obs_reset_audio(ref AudioInfo info);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] internal static partial int obs_open_module(out nint module, string path, string data);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] internal static partial bool obs_init_module(nint module);
    [LibraryImport(Lib)] internal static partial void obs_post_load_modules();
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] internal static partial void obs_add_data_path(string path);
    [LibraryImport(Lib)] internal static partial nint obs_data_create();
    [LibraryImport(Lib)] internal static partial void obs_data_release(nint data);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] internal static partial void obs_data_set_string(nint data, string key, string value);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] internal static partial void obs_data_set_int(nint data, string key, long value);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] internal static partial void obs_data_set_bool(nint data, string key, [MarshalAs(UnmanagedType.I1)] bool value);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint obs_source_create(string id, string name, nint settings, nint hotkeys);
    [LibraryImport(Lib)] internal static partial void obs_source_release(nint source);
    [LibraryImport(Lib)] internal static partial uint obs_source_get_width(nint source);
    [LibraryImport(Lib)] internal static partial uint obs_source_get_height(nint source);
    [LibraryImport(Lib)] internal static partial void obs_set_output_source(uint channel, nint source);
    [LibraryImport(Lib)] internal static partial nint obs_source_get_proc_handler(nint source);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint obs_scene_create(string name);
    [LibraryImport(Lib)] internal static partial nint obs_scene_get_source(nint scene);
    [LibraryImport(Lib)] internal static partial nint obs_scene_add(nint scene, nint source);
    [LibraryImport(Lib)] internal static partial void obs_scene_release(nint scene);
    [LibraryImport(Lib)] internal static partial void obs_sceneitem_set_bounds_type(nint item, int type);
    [LibraryImport(Lib)] internal static partial void obs_sceneitem_set_bounds(nint item, ref Vector2 bounds);
    [StructLayout(LayoutKind.Sequential)] internal struct Vector2 { public float X, Y; }
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] internal static partial bool obs_enum_encoder_types(nuint index, out nint id);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] internal static partial bool obs_enum_input_types(nuint index, out nint id);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint obs_video_encoder_create(string id, string name, nint settings, nint hotkeys);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint obs_audio_encoder_create(string id, string name, nint settings, nuint mixer, nint hotkeys);
    [LibraryImport(Lib)] internal static partial void obs_encoder_set_video(nint encoder, nint video);
    [LibraryImport(Lib)] internal static partial void obs_encoder_set_audio(nint encoder, nint audio);
    [LibraryImport(Lib)] internal static partial void obs_encoder_release(nint encoder);
    [LibraryImport(Lib)] internal static partial nint obs_get_video();
    [LibraryImport(Lib)] internal static partial nint obs_get_audio();
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint obs_output_create(string id, string name, nint settings, nint hotkeys);
    [LibraryImport(Lib)] internal static partial void obs_output_set_video_encoder(nint output, nint encoder);
    [LibraryImport(Lib)] internal static partial void obs_output_set_audio_encoder(nint output, nint encoder, nuint index);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] internal static partial bool obs_output_start(nint output);
    [LibraryImport(Lib)] internal static partial void obs_output_stop(nint output);
    [LibraryImport(Lib)] internal static partial void obs_output_force_stop(nint output);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] internal static partial bool obs_output_active(nint output);
    [LibraryImport(Lib)] internal static partial void obs_output_release(nint output);
    [LibraryImport(Lib)] internal static partial nint obs_output_get_last_error(nint output);
    [LibraryImport(Lib)] internal static partial int obs_output_get_total_frames(nint output);
    [LibraryImport(Lib)] internal static partial int obs_output_get_frames_dropped(nint output);
    [LibraryImport(Lib)] internal static partial nint obs_output_get_proc_handler(nint output);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] [return: MarshalAs(UnmanagedType.I1)] internal static partial bool proc_handler_call(nint handler, string name, ref CallData data);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] [return: MarshalAs(UnmanagedType.I1)] internal static partial bool calldata_get_string(ref CallData data, string name, out nint value);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] [return: MarshalAs(UnmanagedType.I1)] internal static partial bool calldata_get_data(ref CallData data, string name, out byte value, nuint size);
    [LibraryImport(Lib)] internal static partial void bfree(nint ptr);
    internal static string Text(nint pointer) => Marshal.PtrToStringUTF8(pointer) ?? "";
    internal static void Free(ref CallData data) { if (data.Stack != 0) bfree(data.Stack); data = default; }
}

internal sealed class ObsData : IDisposable
{
    public nint Handle { get; } = Native.obs_data_create();
    public ObsData Set(string key, string value) { Native.obs_data_set_string(Handle, key, value); return this; }
    public ObsData Set(string key, long value) { Native.obs_data_set_int(Handle, key, value); return this; }
    public ObsData Set(string key, bool value) { Native.obs_data_set_bool(Handle, key, value); return this; }
    public void Dispose() => Native.obs_data_release(Handle);
}
