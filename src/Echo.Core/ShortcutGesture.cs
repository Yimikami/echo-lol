namespace Echo.Core;

public sealed record ShortcutGesture(uint Modifiers, uint Key, string Text)
{
    public static ShortcutGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint mods = 0;
        foreach (var part in parts.SkipLast(1))
        {
            var flag = part.ToUpperInvariant() switch { "CTRL" or "CONTROL" => 2u, "ALT" => 1u, "SHIFT" => 4u, _ => throw new ArgumentException("Use Ctrl, Alt or Shift with a key.") };
            if ((mods & flag) != 0) throw new ArgumentException("A shortcut modifier cannot be repeated.");
            mods |= flag;
        }
        var key = parts.LastOrDefault()?.ToUpperInvariant() ?? "";
        uint vk;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) vk = key[0];
        else if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var f) && f is >= 1 and <= 24 && f != 12) vk = (uint)(0x70 + f - 1);
        else throw new ArgumentException("Choose a letter, number or function key. F12 is reserved by Windows.");
        if ((mods & 3) == 0 && vk < 0x70) throw new ArgumentException("Add Ctrl or Alt so the shortcut does not take a gameplay key.");
        var labels = new List<string>(); if ((mods & 2) != 0) labels.Add("Ctrl"); if ((mods & 1) != 0) labels.Add("Alt"); if ((mods & 4) != 0) labels.Add("Shift"); labels.Add(key);
        return new(mods, vk, string.Join('+', labels));
    }
}
