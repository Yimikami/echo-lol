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
    private delegate bool EnumProc(nint hwnd, nint param);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumProc callback, nint param);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    internal static CaptureWindow? Find(string? title = null)
    {
        CaptureWindow? found = null;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out var pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                var text = new StringBuilder(1024); GetWindowText(h, text, text.Capacity);
                if (title == null ? !process.ProcessName.Equals("League of Legends", StringComparison.OrdinalIgnoreCase) : !text.ToString().Equals(title, StringComparison.Ordinal)) return true;
                var cls = new StringBuilder(512); GetClassName(h, cls, cls.Capacity);
                found = new(h, (int)pid, text.ToString(), cls.ToString(), process.ProcessName + ".exe");
                return false;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return true; }
        }, 0);
        return found;
    }
}
