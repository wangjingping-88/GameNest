using System.Diagnostics;
using System.Runtime.InteropServices;
using GameNest.Application;
using GameNest.Domain;
using GameNest.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Telemetry.Tests;

public sealed class WindowsOverlayControllerTests
{
    private const int GwlExstyle = -20;
    private const long RequiredExtendedStyles =
        0x00000008L | 0x00000020L | 0x00000080L | 0x00080000L | 0x08000000L;

    [Fact]
    public async Task IndependentOverlayConnectsReceivesFrameAndShutsDown()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Overlay", "GameNest.Overlay.exe");
        Assert.True(File.Exists(executable), $"测试覆盖层不存在：{executable}");
        await using var controller = new WindowsOverlayController(
            new OverlayProcessOptions(executable, TimeSpan.FromSeconds(5)),
            NullLogger<WindowsOverlayController>.Instance);
        await controller.EnsureStartedAsync(TestContext.Current.CancellationToken);
        var profile = new OverlayProfile(
            Guid.NewGuid(),
            null,
            true,
            OverlayPosition.TopRight,
            75,
            88,
            true,
            true,
            true,
            true,
            "Ctrl+Shift+F24",
            true,
            DateTimeOffset.UtcNow);
        var gameId = Guid.NewGuid();
        var frame = new OverlayFrame(
            new GameWindowSnapshot(
                123,
                new GameWindowBounds(100, 100, 1280, 720),
                96,
                false,
                false,
                false),
            profile,
            new PerformanceSnapshot(
                gameId,
                DateTimeOffset.UtcNow,
                TelemetryMetric.Available(60),
                TelemetryMetric.Available(10),
                TelemetryMetric.Unavailable("测试降级"),
                TelemetryMetric.Available(512 * 1024 * 1024)),
            true);

        await controller.UpdateAsync(frame, TestContext.Current.CancellationToken);
        var overlayWindow = await WaitForOverlayWindowAsync(TestContext.Current.CancellationToken);
        Assert.Equal(OverlayControllerState.Ready, controller.Status.State);
        Assert.NotEqual(overlayWindow, GetForegroundWindow());
        Assert.Equal(
            RequiredExtendedStyles,
            GetWindowLongPtrW(overlayWindow, GwlExstyle).ToInt64() & RequiredExtendedStyles);
        Assert.True(IsWindowVisible(overlayWindow));
        AssertWindowPosition(overlayWindow, expectedLeft: 1071, expectedTop: 112);

        var movedFrame = frame with
        {
            Window = frame.Window with
            {
                ContentBounds = new GameWindowBounds(300, 200, 1024, 768),
                Dpi = 144,
            },
        };
        var relocation = Stopwatch.StartNew();
        await controller.UpdateAsync(movedFrame, TestContext.Current.CancellationToken);
        while (relocation.Elapsed < TimeSpan.FromSeconds(1))
        {
            if (TryGetPhysicalWindowRect(overlayWindow, out var movedBounds) &&
                Math.Abs(movedBounds.Left - 860) <= 2 &&
                Math.Abs(movedBounds.Top - 218) <= 2)
            {
                break;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        relocation.Stop();
        AssertWindowPosition(overlayWindow, expectedLeft: 860, expectedTop: 218);
        Assert.True(relocation.Elapsed < TimeSpan.FromSeconds(1));

        await controller.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Equal(OverlayControllerState.Stopped, controller.Status.State);
    }

    private static async Task<nint> WaitForOverlayWindowAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var window = FindWindowW("GameNest.Overlay.Window", "GameNest Overlay");
            if (window != nint.Zero && IsWindowVisible(window))
            {
                return window;
            }

            await Task.Delay(25, cancellationToken);
        }

        throw new TimeoutException("覆盖层窗口未在 1 秒内显示。");
    }

    private static void AssertWindowPosition(nint window, int expectedLeft, int expectedTop)
    {
        Assert.True(TryGetPhysicalWindowRect(window, out var bounds));
        Assert.InRange(bounds.Left, expectedLeft - 2, expectedLeft + 2);
        Assert.InRange(bounds.Top, expectedTop - 2, expectedTop + 2);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string className, string windowName);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    private static bool TryGetPhysicalWindowRect(nint window, out Rect rect) =>
        DwmGetWindowAttribute(window, 9, out rect, Marshal.SizeOf<Rect>()) == 0;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        uint attribute,
        out Rect value,
        int size);
}
