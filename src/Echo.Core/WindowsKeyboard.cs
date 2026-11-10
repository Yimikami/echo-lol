namespace Echo.Core;

// Maps Raw Input virtual keys and scan flags to stable overlay IDs.
public static class WindowsKeyboard
{
    public static string? Control(int key, int scan, int flags)
    {
        bool extended = (flags & 2) != 0;
        if (!extended && key is >= 186 and <= 226 && OemControl(scan) is { } physical) return physical;
        return key switch
        {
            8 => "Backspace", 9 => "Tab", 13 => extended ? "NumpadEnter" : "Enter",
            16 => scan == 0x36 ? "RightShift" : "Shift", 160 => "Shift", 161 => "RightShift",
            17 => extended ? "RightCtrl" : "Ctrl", 162 => "Ctrl", 163 => "RightCtrl",
            18 => extended ? "RightAlt" : "Alt", 164 => "Alt", 165 => "RightAlt",
            19 => "Pause", 20 => "Caps", 27 => "Escape", 32 => "Space",
            12 => "Numpad5", 33 => extended ? "PageUp" : "Numpad9", 34 => extended ? "PageDown" : "Numpad3",
            35 => extended ? "End" : "Numpad1", 36 => extended ? "Home" : "Numpad7",
            37 => extended ? "Left" : "Numpad4", 38 => extended ? "Up" : "Numpad8",
            39 => extended ? "Right" : "Numpad6", 40 => extended ? "Down" : "Numpad2",
            44 => "PrintScreen", 45 => extended ? "Insert" : "Numpad0", 46 => extended ? "Delete" : "NumpadDecimal",
            >= 48 and <= 57 or >= 65 and <= 90 => ((char)key).ToString(),
            91 => "LeftWindows", 92 => "RightWindows", 93 => "Menu",
            >= 96 and <= 105 => "Numpad" + (key - 96),
            106 => "NumpadMultiply", 107 => "NumpadAdd", 109 => "NumpadSubtract",
            108 or 110 => "NumpadDecimal", 111 => "NumpadDivide",
            >= 112 and <= 123 => "F" + (key - 111),
            144 => "NumLock", 145 => "ScrollLock",
            186 => "Semicolon", 187 => "Equals", 188 => "Comma", 189 => "Minus", 190 => "Period", 191 => "Slash",
            192 => "Backtick", 219 => "LeftBracket", 220 => "Backslash", 221 => "RightBracket", 222 => "Quote",
            226 => "IsoBackslash", _ => null
        };
    }

    private static string? OemControl(int scan) => scan switch
    {
        0x0C => "Minus", 0x0D => "Equals", 0x1A => "LeftBracket", 0x1B => "RightBracket",
        0x27 => "Semicolon", 0x28 => "Quote", 0x29 => "Backtick", 0x2B => "Backslash",
        0x33 => "Comma", 0x34 => "Period", 0x35 => "Slash", 0x56 => "IsoBackslash", _ => null
    };
}
