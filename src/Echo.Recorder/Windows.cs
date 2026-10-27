using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Echo.Recorder;

internal sealed record CaptureWindow(nint Handle, int ProcessId, string Title, string Class, string Executable)
{
    // OBS's window selector escapes ':' and '#' inside all three fields.
    public string Selector => string.Join(":", new[] { Title, Class, Executable }.Select(s => s.Replace("#", "#22").Replace(":", "#3A")));
}

internal static class Windows
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    { public uint Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
    { public uint Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key; }
    [DllImport("user32")] private static extern nint GetForegroundWindow();
    [DllImport("user32")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayDevicesW(string device, uint index, ref DisplayDevice data, uint flags);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    private delegate bool EnumProc(nint hwnd, nint param);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumProc callback, nint param);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    internal static bool IsForeground(int processId)
    { var window = GetForegroundWindow(); GetWindowThreadProcessId(window, out var id); return id == processId; }
    internal static nint MonitorHandle(nint hwnd) => MonitorFromWindow(hwnd, 2);
    internal static string MonitorId(CaptureWindow window)
    {
        var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>(), Device = "" };
        if (!GetMonitorInfoW(MonitorFromWindow(window.Handle, 2), ref monitor)) throw new InvalidOperationException("Could not identify the game's monitor.");
        var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>(), Name = "", Description = "", Id = "", Key = "" };
        return EnumDisplayDevicesW(monitor.Device, 0, ref device, 1) ? device.Id : monitor.Device;
    }
    internal static CaptureWindow? Find(string? title = null)
    {
        CaptureWindow? found = null;
        long largestArea = 0;
        HashSet<int>? gameIds = null;
        if (title == null)
        {
            var processes = Process.GetProcessesByName("League of Legends");
            gameIds = processes.Select(p => p.Id).ToHashSet();
            foreach (var p in processes) p.Dispose();
            if (gameIds.Count == 0) return null;
        }
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out var pid);
            if (gameIds != null && !gameIds.Contains((int)pid)) return true;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                var text = new StringBuilder(1024); GetWindowText(h, text, text.Capacity);
                if (title == null ? !process.ProcessName.Equals("League of Legends", StringComparison.OrdinalIgnoreCase) : !text.ToString().Equals(title, StringComparison.Ordinal)) return true;
                var cls = new StringBuilder(512); GetClassName(h, cls, cls.Capacity);
                if (!GetWindowRect(h, out var rect)) return true;
                var area = (long)Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
                if (area > largestArea)
                {
                    largestArea = area;
                    found = new(h, (int)pid, text.ToString(), cls.ToString(), process.ProcessName + ".exe");
                }
                return true;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return true; }
        }, 0);
        return found;
    }
}
