using System.Runtime.InteropServices;
using Echo.Core;

namespace Echo.App;

internal static class CaptureHardwareProbe
{
    // Probe once, on demand. No background polling or recording benchmark.
    private static readonly Lazy<Task<CaptureHardware>> Cached = new(() => Task.Run(Read));
    internal static Task<CaptureHardware> GetAsync() => Cached.Value;

    private static CaptureHardware Read()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        long ram = GlobalMemoryStatusEx(ref memory) ? (long)(memory.TotalPhysical / 1048576) : 0;
        string name = "Graphics information unavailable";
        long videoMemory = 0;
        bool found = false;
        nint factory = 0;
        try
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            if (CreateDXGIFactory1(ref iid, out factory) >= 0)
            {
                // libobs uses adapter 0. Do not recommend based on an unrelated, faster secondary GPU.
                var enumerate = Method<EnumAdapters>(factory, 12);
                if (enumerate(factory, 0, out var adapter) >= 0)
                {
                    try
                    {
                        if (Method<GetDescription>(adapter, 10)(adapter, out var desc) >= 0 && (desc.Flags & 3) == 0)
                        {
                            name = desc.Description.Trim();
                            videoMemory = (long)(desc.DedicatedVideoMemory / 1048576);
                            found = true;
                        }
                    }
                    finally { Marshal.Release(adapter); }
                }
            }
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException) { }
        finally { if (factory != 0) Marshal.Release(factory); }
        return new(name, videoMemory, ram, Environment.ProcessorCount, GetSystemMetrics(0), GetSystemMetrics(1), found);
    }

    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters(nint self, uint index, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDescription(nint self, out AdapterDescription description);

    // DXGI_ADAPTER_DESC1: https://learn.microsoft.com/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSystemId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh; public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out nint factory);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
