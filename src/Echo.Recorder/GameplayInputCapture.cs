using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Echo.Core;

namespace Echo.Recorder;

/// <summary>Optional, read-only Win32 input observer. No hooks, injection, text or mouse coordinates.</summary>
internal sealed class GameplayInputCapture : IDisposable
{
    private readonly Thread _thread;
    private InputWindow? _window;
    private readonly ManualResetEventSlim _ready = new();
    private Exception? _failure;
    private volatile bool _stopping;
    public string? Error => _failure?.Message ?? _window?.Failure;
    public GameplayInputCapture(int processId, string file, Stopwatch clock)
    {
        _thread = new Thread(() =>
        {
            try { using var window = new InputWindow(processId, file, clock); _window = window; _ready.Set(); if (!_stopping) Application.Run(); }
            catch (Exception e) { _failure = e; _ready.Set(); }
        }) { IsBackground = true, Name = "Echo optional gameplay inputs" };
        _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
        if (!_ready.Wait(3000)) { _stopping = true; throw new IOException("Gameplay input capture did not start."); }
        if (_failure != null) throw new IOException("Gameplay inputs unavailable: " + _failure.Message, _failure);
    }
    public void Dispose()
    {
        _stopping = true;
        if (_window != null) PostMessageW(_window.Handle, 0x8001, 0, 0);
        _thread.Join(3000); _ready.Dispose();
    }
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint hwnd, uint msg, nint w, nint l);
    [StructLayout(LayoutKind.Sequential)] private struct Device { public ushort Page, Usage; public uint Flags; public nint Target; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] private static extern uint GetRegisteredRawInputDevices(nint devices, ref uint count, uint size);
    internal static bool VerifyRegistration(string folder)
    {
        var file = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".jsonl");
        bool registered;
        using (var capture = new GameplayInputCapture(Environment.ProcessId, file, Stopwatch.StartNew())) { uint count = 0; GetRegisteredRawInputDevices(0, ref count, (uint)Marshal.SizeOf<Device>()); registered = count == 2; }
        uint after = 0; GetRegisteredRawInputDevices(0, ref after, (uint)Marshal.SizeOf<Device>());
        return registered && after == 0;
    }
    private sealed class InputWindow : NativeWindow, IDisposable
    {
        private readonly int _process;
        private readonly Stopwatch _clock;
        private readonly StreamWriter _writer;
        private readonly HashSet<string> _held = [];
        private readonly nint _buffer = Marshal.AllocHGlobal(128);
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
        private bool _chat, _enterDown;
        internal string? Failure { get; private set; }
        private static readonly string[] MouseKeys = ["MouseLeft", "MouseRight", "MouseMiddle", "Mouse4", "Mouse5"];
        private static readonly JsonSerializerOptions Compact = new(JsonStore.Options) { WriteIndented = false };
        internal InputWindow(int process, string file, Stopwatch clock)
        {
            _process = process; _clock = clock;
            _writer = new(new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            CreateHandle(new CreateParams { Caption = "Echo gameplay inputs", Parent = new nint(-3) });
            Device[] devices = [new() { Page = 1, Usage = 6, Flags = 0x100, Target = Handle }, new() { Page = 1, Usage = 2, Flags = 0x100, Target = Handle }];
            if (!RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf<Device>())) { Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
            _timer.Tick += (_, _) => { try { if (!Windows.IsForeground(_process)) Release(); _writer.Flush(); } catch (IOException ex) { Failure = ex.Message; Application.ExitThread(); } }; _timer.Start();
        }
        private void Write(string key, bool down)
        {
            if (!GameplayInputs.Allowed.Contains(key)) return;
            if (down ? !_held.Add(key) : !_held.Remove(key)) return; // excludes OS repeat
            _writer.WriteLine(JsonSerializer.Serialize(new InputSample(_clock.Elapsed.TotalSeconds, key, down), Compact));
        }
        private void Release() { foreach (var key in _held.ToArray()) Write(key, false); }
        protected override void WndProc(ref Message m)
        {
            try { ReadInput(ref m); }
            catch (IOException ex) { Failure = ex.Message; Application.ExitThread(); }
            base.WndProc(ref m);
        }
        private void ReadInput(ref Message m)
        {
            if (m.Msg == 0x8001) { Application.ExitThread(); return; }
            if (m.Msg == 0x00FF)
            {
                if (!Windows.IsForeground(_process)) { Release(); _enterDown = false; return; }
                uint size = 128, header = (uint)(8 + 2 * nint.Size);
                if (GetRawInputData(m.LParam, 0x10000003, _buffer, ref size, header) != uint.MaxValue)
                {
                    var type = Marshal.ReadInt32(_buffer); var data = _buffer + (int)header;
                    if (type == 1)
                    {
                        var vk = (ushort)Marshal.ReadInt16(data, 6); var up = (Marshal.ReadInt16(data, 2) & 1) != 0;
                        if (vk == 13) { if (up) _enterDown = false; else if (!_enterDown) { _enterDown = true; _chat = !_chat; Release(); } }
                        else if (vk == 27 && !up) _chat = false;
                        else if (!_chat)
                        {
                            var key = vk switch { 16 or 160 or 161 => "Shift", 17 or 162 or 163 => "Ctrl", 18 or 164 or 165 => "Alt", 32 => "Space", 9 => "Tab",8=>"Backspace",192=>"Backtick",20=>"Caps",37=>"Left",38=>"Up",39=>"Right",40=>"Down",>=112 and <=123=>"F"+(vk-111), >= 48 and <= 90 => ((char)vk).ToString(), _ => "" };
                            Write(key, !up);
                        }
                    }
                    else if (type == 0 && !_chat)
                    {
                        var flags = (ushort)Marshal.ReadInt16(data, 4);
                        for (int i = 0; i < MouseKeys.Length; i++) { if ((flags & (1 << (i * 2))) != 0) Write(MouseKeys[i], true); if ((flags & (2 << (i * 2))) != 0) Write(MouseKeys[i], false); }
                        if ((flags & 0x400) != 0) { var key = Marshal.ReadInt16(data, 6) > 0 ? "WheelUp" : "WheelDown"; var at = _clock.Elapsed.TotalSeconds; _writer.WriteLine(JsonSerializer.Serialize(new InputSample(at, key, true), Compact)); _writer.WriteLine(JsonSerializer.Serialize(new InputSample(at + .1, key, false), Compact)); }
                    }
                }
            }
        }
        public void Dispose()
        {
            _timer.Stop(); _timer.Dispose();
            try { Release(); _writer.Dispose(); }
            catch (IOException ex) { Failure = ex.Message; }
            finally
            {
                Device[] devices = [new() { Page = 1, Usage = 6, Flags = 1 }, new() { Page = 1, Usage = 2, Flags = 1 }];
                RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf<Device>()); DestroyHandle(); Marshal.FreeHGlobal(_buffer);
            }
        }
    }
}
