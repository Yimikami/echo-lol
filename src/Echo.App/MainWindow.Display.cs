using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Echo.App;

public partial class MainWindow
{
    private sealed record WindowPlacement(WindowState State, Rect Bounds, ResizeMode ResizeMode, WindowChrome? Chrome);

    private WindowPlacement? _beforeFullscreen;
    private Size _workspaceMinimum;

    private void ConfigureDisplayFrame()
    {
        _workspaceMinimum = new(MinWidth, MinHeight);
        SourceInitialized += (_, _) => UpdateDisplayFrame();
        StateChanged += (_, _) => UpdateDisplayFrame();
        SizeChanged += (_, _) => UpdateDisplayFrame();
        LocationChanged += (_, _) => UpdateDisplayFrame();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            UpdateDisplayFrame();
            var handle = new WindowInteropHelper(this).Handle;
            if (_fullscreen && WindowDisplay.Areas(handle) is { } areas && WindowDisplay.ClientBounds(handle) != areas.Monitor)
                WindowDisplay.Place(handle, areas.Monitor);
        }, DispatcherPriority.Loaded);
    }

    private void UpdateDisplayFrame()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Data = Geometry.Parse(maximized ? "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z" : "M0.5,0.5 H9.5 V9.5 H0.5 Z");
        MaximizeButton.ToolTip = maximized ? "Restore down" : "Maximize";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, (string)MaximizeButton.ToolTip);
        var handle = new WindowInteropHelper(this).Handle;
        if (WindowDisplay.Areas(handle) is not { } areas) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        // On a scaled display the work area can be smaller than the usual window minimum.
        MinWidth = Math.Min(_workspaceMinimum.Width, Math.Floor(areas.Work.Width / dpi.DpiScaleX));
        MinHeight = Math.Min(_workspaceMinimum.Height, Math.Floor(areas.Work.Height / dpi.DpiScaleY));

        // Windows can maximize a custom-chrome window with resize borders outside the work area.
        WindowFrame.Padding = !_fullscreen && WindowState == WindowState.Maximized
            ? WindowDisplay.VisiblePadding(WindowDisplay.ClientBounds(handle), areas.Work, dpi) : new Thickness(0);
    }

    private void SetPlaybackWindowMode(bool fullscreen)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (fullscreen)
        {
            var areas = WindowDisplay.Areas(handle);
            _beforeFullscreen = new(WindowState, RestoreBounds, ResizeMode, WindowChrome.GetWindowChrome(this));
            WindowState = WindowState.Normal;
            WindowChrome.SetWindowChrome(this, null);
            ResizeMode = ResizeMode.NoResize;
            UpdateDisplayFrame();
            if (areas == null || !WindowDisplay.Place(handle, areas.Value.Monitor)) WindowState = WindowState.Maximized;
            if (IsVisible) WindowDisplay.MarkFullscreen(handle, true);
        }
        else if (_beforeFullscreen is { } prior)
        {
            _beforeFullscreen = null;
            WindowState = WindowState.Normal;
            ResizeMode = prior.ResizeMode;
            WindowChrome.SetWindowChrome(this, prior.Chrome);
            if (!prior.Bounds.IsEmpty)
            {
                Left = prior.Bounds.Left;
                Top = prior.Bounds.Top;
                Width = prior.Bounds.Width;
                Height = prior.Bounds.Height;
            }
            WindowState = prior.State;
            if (IsVisible) WindowDisplay.MarkFullscreen(handle, false);
            UpdateDisplayFrame();
        }
    }
}

internal static class WindowDisplay
{
    internal static (Rect Monitor, Rect Work)? Areas(nint window)
    {
        if (window == 0) return null;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromWindow(window, 2), ref info)
            ? (info.Monitor.ToRect(), info.Work.ToRect()) : null;
    }

    internal static Rect ClientBounds(nint window)
    {
        var origin = new NativePoint();
        return GetClientRect(window, out var client) && ClientToScreen(window, ref origin)
            ? new Rect(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top) : Rect.Empty;
    }

    internal static Thickness VisiblePadding(Rect client, Rect work, DpiScale dpi)
    {
        if (client.IsEmpty) return new(0);
        return new(Math.Max(0, work.Left - client.Left) / dpi.DpiScaleX,
            Math.Max(0, work.Top - client.Top) / dpi.DpiScaleY,
            Math.Max(0, client.Right - work.Right) / dpi.DpiScaleX,
            Math.Max(0, client.Bottom - work.Bottom) / dpi.DpiScaleY);
    }

    internal static bool Place(nint window, Rect bounds)
    {
        const uint NoZOrder = 0x0004, NoActivate = 0x0010, FrameChanged = 0x0020;
        return SetWindowPos(window, 0, (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height,
            NoZOrder | NoActivate | FrameChanged);
    }

    internal static void MarkFullscreen(nint window, bool fullscreen)
    {
        object? taskbar = null;
        try
        {
            taskbar = new TaskbarList();
            var shell = (ITaskbarList2)taskbar;
            shell.HrInit();
            shell.MarkFullscreenWindow(window, fullscreen);
        }
        catch (COMException) { /* Windows also detects monitor-sized windows when Explorer is available. */ }
        finally { if (taskbar != null) Marshal.ReleaseComObject(taskbar); }
    }

    [ComImport, Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]
    private class TaskbarList { }

    [ComImport, Guid("602D4995-B13A-429B-A66E-1935E44F4317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList2
    {
        void HrInit();
        void AddTab(nint window);
        void DeleteTab(nint window);
        void ActivateTab(nint window);
        void SetActiveAlt(nint window);
        void MarkFullscreenWindow(nint window, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
