using System.Runtime.InteropServices;
using GameNest.Domain;

namespace GameNest.Telemetry;

public static class WindowsHotkeyMapper
{
    public const uint NoRepeatModifier = 0x4000;

    public static bool TryMap(OverlayHotkey hotkey, out uint modifiers, out uint virtualKey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        modifiers = NoRepeatModifier;
        if (hotkey.Modifiers.HasFlag(OverlayHotkeyModifiers.Alt))
        {
            modifiers |= 0x0001;
        }

        if (hotkey.Modifiers.HasFlag(OverlayHotkeyModifiers.Control))
        {
            modifiers |= 0x0002;
        }

        if (hotkey.Modifiers.HasFlag(OverlayHotkeyModifiers.Shift))
        {
            modifiers |= 0x0004;
        }

        if (hotkey.Modifiers.HasFlag(OverlayHotkeyModifiers.Windows))
        {
            modifiers |= 0x0008;
        }

        if (hotkey.Key.Length == 1 && char.IsAsciiLetterOrDigit(hotkey.Key[0]))
        {
            virtualKey = char.ToUpperInvariant(hotkey.Key[0]);
            return true;
        }

        if (hotkey.Key.Length is 2 or 3 &&
            hotkey.Key[0] == 'F' &&
            int.TryParse(hotkey.Key.AsSpan(1), out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            virtualKey = checked((uint)(0x70 + functionKey - 1));
            return true;
        }

        virtualKey = 0;
        return false;
    }
}

internal static class WindowsHotkeyProbe
{
    public static Task<bool> IsAvailableAsync(OverlayHotkey hotkey, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                if (!WindowsHotkeyMapper.TryMap(hotkey, out var modifiers, out var virtualKey))
                {
                    return false;
                }

                const int probeId = 0x474E;
                var registered = RegisterHotKey(nint.Zero, probeId, modifiers, virtualKey);
                if (registered)
                {
                    _ = UnregisterHotKey(nint.Zero, probeId);
                }

                return registered;
            },
            cancellationToken);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
