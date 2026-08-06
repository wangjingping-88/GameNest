namespace GameNest.Domain;

[Flags]
public enum OverlayHotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

public sealed record OverlayHotkey(
    OverlayHotkeyModifiers Modifiers,
    string Key,
    string DisplayText)
{
    public static OverlayHotkey Default { get; } = Parse("Ctrl+Shift+F12");

    public static OverlayHotkey Parse(string value)
    {
        if (!TryParse(value, out var hotkey))
        {
            throw new ArgumentException(
                "快捷键格式无效。请使用 Ctrl、Shift、Alt 或 Win 加一个字母、数字或 F1-F24。",
                nameof(value));
        }

        return hotkey;
    }

    public static bool TryParse(string? value, out OverlayHotkey hotkey)
    {
        hotkey = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var modifiers = OverlayHotkeyModifiers.None;
        string? key = null;
        foreach (var rawPart in value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.ToUpperInvariant();
            switch (part)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= OverlayHotkeyModifiers.Control;
                    break;
                case "SHIFT":
                    modifiers |= OverlayHotkeyModifiers.Shift;
                    break;
                case "ALT":
                    modifiers |= OverlayHotkeyModifiers.Alt;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= OverlayHotkeyModifiers.Windows;
                    break;
                default:
                    if (key is not null || !IsSupportedKey(part))
                    {
                        return false;
                    }

                    key = part;
                    break;
            }
        }

        if (modifiers == OverlayHotkeyModifiers.None || key is null)
        {
            return false;
        }

        var labels = new List<string>(5);
        if (modifiers.HasFlag(OverlayHotkeyModifiers.Control))
        {
            labels.Add("Ctrl");
        }

        if (modifiers.HasFlag(OverlayHotkeyModifiers.Shift))
        {
            labels.Add("Shift");
        }

        if (modifiers.HasFlag(OverlayHotkeyModifiers.Alt))
        {
            labels.Add("Alt");
        }

        if (modifiers.HasFlag(OverlayHotkeyModifiers.Windows))
        {
            labels.Add("Win");
        }

        labels.Add(key.Length == 1 ? key : key.ToUpperInvariant());
        hotkey = new OverlayHotkey(modifiers, key, string.Join('+', labels));
        return true;
    }

    private static bool IsSupportedKey(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            return true;
        }

        return key.Length is 2 or 3 &&
               key[0] == 'F' &&
               int.TryParse(key.AsSpan(1), out var functionKey) &&
               functionKey is >= 1 and <= 24;
    }
}
