using System.Runtime.InteropServices;
using Echo.Core;

namespace Echo.Recorder;

internal sealed class GlobalShortcut : NativeWindow, IDisposable
{
    private const int Id = 0x4543, WmHotkey = 0x0312;
    private readonly Action _save;
    private ShortcutGesture? _current;
    private string? _lastRequested;
    public string? Error { get; private set; }
    public string? Active => _current?.Text;
    [DllImport("user32", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(nint window, int id);
    public GlobalShortcut(Action save)
    {
        _save = save;
        CreateHandle(new CreateParams { Caption = "Echo manual moment", Parent = new nint(-3) });
    }
    public void Refresh(string text)
    {
        if (_lastRequested == text) return;
        _lastRequested = text; Error = null;
        ShortcutGesture? requested;
        try { requested = ShortcutGesture.Parse(text); }
        catch (ArgumentException e) { Error = e.Message; return; }
        var previous = _current;
        if (_current != null) UnregisterHotKey(Handle, Id);
        _current = null;
        if (requested == null) return;
        if (RegisterHotKey(Handle, Id, requested.Modifiers | 0x4000, requested.Key)) { _current = requested; return; }
        Error = $"{requested.Text} is already in use. Choose another shortcut.";
        if (previous != null && RegisterHotKey(Handle, Id, previous.Modifiers | 0x4000, previous.Key)) _current = previous;
    }
    protected override void WndProc(ref Message m) { if (m.Msg == WmHotkey && m.WParam == Id) _save(); base.WndProc(ref m); }
    public void Dispose() { if (_current != null) UnregisterHotKey(Handle, Id); DestroyHandle(); }
    internal static bool VerifyRegistration()
    {
        using var first = new GlobalShortcut(() => { }); using var second = new GlobalShortcut(() => { });
        const string chord = "Ctrl+Alt+Shift+F23";
        first.Refresh(chord); second.Refresh(chord);
        var conflict = first.Active == chord && second.Error != null;
        first.Refresh(""); second.Refresh(""); second.Refresh(chord);
        return conflict && second.Active == chord;
    }
}
