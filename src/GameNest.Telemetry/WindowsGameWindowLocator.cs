using System.Runtime.InteropServices;
using GameNest.Application;

namespace GameNest.Telemetry;

public sealed class WindowsGameWindowLocator : IGameWindowLocator
{
    private const int GwlExstyle = -20;
    private const long WsExToolwindow = 0x00000080L;
    private const uint DwmwaCloaked = 14;
    private const uint MonitorDefaultToNearest = 2;

    public Task<GameWindowSnapshot?> FindPrimaryWindowAsync(
        GameRuntimeSnapshot runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return Task.Run(() => Find(runtime, cancellationToken), cancellationToken);
    }

    private static GameWindowSnapshot? Find(
        GameRuntimeSnapshot runtime,
        CancellationToken cancellationToken)
    {
        var processIds = runtime.Processes
            .Where(static process => process.Confidence == Domain.GameProcessConfidence.Confirmed)
            .Select(static process => process.ProcessId)
            .ToHashSet();
        if (processIds.Count == 0)
        {
            return null;
        }

        var foreground = GetForegroundWindow();
        var candidates = new List<(long Score, GameWindowSnapshot Window)>();
        _ = EnumWindows(
            (window, parameter) =>
            {
                _ = parameter;
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                _ = GetWindowThreadProcessId(window, out var processId);
                if (!processIds.Contains(checked((int)processId)) ||
                    !IsWindowVisible(window) ||
                    (GetWindowLongPtrW(window, GwlExstyle).ToInt64() & WsExToolwindow) != 0 ||
                    IsCloaked(window))
                {
                    return true;
                }

                if (!TryGetClientBounds(window, out var bounds) || bounds.Width <= 0 || bounds.Height <= 0)
                {
                    return true;
                }

                var snapshot = new GameWindowSnapshot(
                    window.ToInt64(),
                    bounds,
                    GetDpiForWindow(window) is var dpi && dpi > 0 ? dpi : 96,
                    window == foreground,
                    IsIconic(window),
                    CoversMonitor(window, bounds));
                var area = (long)bounds.Width * bounds.Height;
                var score = area +
                            (snapshot.IsForeground ? long.MaxValue / 4 : 0) +
                            (processId == runtime.PrimaryProcessId ? long.MaxValue / 8 : 0);
                candidates.Add((score, snapshot));
                return true;
            },
            nint.Zero);

        cancellationToken.ThrowIfCancellationRequested();
        return candidates.OrderByDescending(static candidate => candidate.Score).FirstOrDefault().Window;
    }

    private static bool TryGetClientBounds(nint window, out GameWindowBounds bounds)
    {
        bounds = null!;
        if (!GetClientRect(window, out var client))
        {
            return false;
        }

        var topLeft = new Point { X = client.Left, Y = client.Top };
        var bottomRight = new Point { X = client.Right, Y = client.Bottom };
        if (!ClientToScreen(window, ref topLeft) || !ClientToScreen(window, ref bottomRight))
        {
            return false;
        }

        bounds = new GameWindowBounds(
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);
        return true;
    }

    private static bool IsCloaked(nint window)
    {
        var cloaked = 0;
        return DwmGetWindowAttribute(window, DwmwaCloaked, out cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    private static bool CoversMonitor(nint window, GameWindowBounds bounds)
    {
        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = checked((uint)Marshal.SizeOf<MonitorInfo>()) };
        if (monitor == nint.Zero || !GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }

        const int tolerance = 2;
        return Math.Abs(bounds.Left - info.Monitor.Left) <= tolerance &&
               Math.Abs(bounds.Top - info.Monitor.Top) <= tolerance &&
               Math.Abs(bounds.Width - (info.Monitor.Right - info.Monitor.Left)) <= tolerance &&
               Math.Abs(bounds.Height - (info.Monitor.Bottom - info.Monitor.Top)) <= tolerance;
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref Point point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        uint attribute,
        out int value,
        int size);
}
