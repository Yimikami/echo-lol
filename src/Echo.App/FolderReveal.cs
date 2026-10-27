using System.IO;
using System.Runtime.InteropServices;

namespace Echo.App;

internal static class FolderReveal
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint ILCreateFromPathW(string path);
    [DllImport("shell32.dll")] private static extern void ILFree(nint item);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(nint item, uint count, nint children, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool SHGetPathFromIDListW(nint item, System.Text.StringBuilder path);
    internal static bool VerifyPath(string path)
    {
        var item = ILCreateFromPathW(path); if (item == 0) return false;
        try { var resolved = new System.Text.StringBuilder(32768); return SHGetPathFromIDListW(item, resolved) && string.Equals(Path.GetFullPath(path), resolved.ToString(), StringComparison.OrdinalIgnoreCase); }
        finally { ILFree(item); }
    }
    internal static void Show(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("The video file is unavailable.");
        var item = ILCreateFromPathW(path);
        if (item == 0) throw new IOException("Windows could not locate this video.");
        try { Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, 0, 0)); }
        finally { ILFree(item); }
    }
}
