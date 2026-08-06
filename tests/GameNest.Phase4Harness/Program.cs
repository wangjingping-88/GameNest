using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using GameNest.Application;
using GameNest.Domain;
using GameNest.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length == 0 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("用法：GameNest.Phase4Harness.exe <渲染测试程序路径> [--borderless]");
    return 2;
}

_ = NativeMethods.SetProcessDpiAwarenessContext(new nint(-4));
var probePath = Path.GetFullPath(args[0]);
var borderless = args.Any(static argument => argument.Equals("--borderless", StringComparison.OrdinalIgnoreCase));
var startedAtUtc = DateTimeOffset.UtcNow;
using var probe = Process.Start(
    new ProcessStartInfo
    {
        FileName = probePath,
        Arguments = borderless ? "--borderless" : string.Empty,
        UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(probePath)!,
    });
if (probe is null)
{
    Console.Error.WriteLine("渲染测试程序启动失败。");
    return 3;
}

WindowsOverlayController? overlay = null;
WindowsPerformanceTelemetry? telemetry = null;
try
{
    for (var attempt = 0; attempt < 60 && probe.MainWindowHandle == nint.Zero; attempt++)
    {
        await Task.Delay(50);
        probe.Refresh();
    }

    if (probe.HasExited || probe.MainWindowHandle == nint.Zero)
    {
        Console.Error.WriteLine($"测试程序未创建窗口，退出码：{(probe.HasExited ? probe.ExitCode : -1)}。");
        return 4;
    }

    var gameId = Guid.NewGuid();
    var trackedProcess = new TrackedGameProcess(
        probe.Id,
        null,
        probe.ProcessName,
        probePath,
        probe.StartTime.ToUniversalTime(),
        GameProcessConfidence.Confirmed);
    var runtime = new GameRuntimeSnapshot(
        gameId,
        GameRuntimeState.Running,
        probe.Id,
        GameProcessConfidence.Confirmed,
        DateTimeOffset.UtcNow,
        [trackedProcess]);
    var locator = new WindowsGameWindowLocator();
    var initialWindow = await locator.FindPrimaryWindowAsync(runtime, CancellationToken.None);
    if (initialWindow is null)
    {
        Console.Error.WriteLine("正式窗口定位器未找到测试窗口。");
        return 5;
    }

    telemetry = new WindowsPerformanceTelemetry(
        PresentMonOptions.CreateDefault(),
        NullLoggerFactory.Instance,
        NullLogger<WindowsPerformanceTelemetry>.Instance);
    overlay = new WindowsOverlayController(
        OverlayProcessOptions.CreateDefault(),
        NullLogger<WindowsOverlayController>.Instance);
    var capability = await telemetry.CheckCapabilityAsync(CancellationToken.None);
    await telemetry.StartAsync(new TelemetryTarget(gameId, probe.Id, [probe.Id]), CancellationToken.None);
    var profile = new OverlayProfile(
        Guid.NewGuid(),
        null,
        true,
        OverlayPosition.TopRight,
        100,
        88,
        true,
        true,
        true,
        true,
        "Ctrl+Shift+F23",
        false,
        DateTimeOffset.UtcNow);
    var initialSnapshot = telemetry.Current ?? CreateUnavailableSnapshot(gameId);
    await overlay.UpdateAsync(new OverlayFrame(initialWindow, profile, initialSnapshot, true), CancellationToken.None);
    await Task.Delay(150);

    var overlayWindow = NativeMethods.FindWindowW("GameNest.Overlay.Window", "GameNest Overlay");
    if (overlayWindow == nint.Zero || !NativeMethods.IsWindowVisible(overlayWindow))
    {
        Console.Error.WriteLine("独立覆盖层未显示。");
        return 6;
    }

    _ = NativeMethods.GetWindowThreadProcessId(overlayWindow, out var overlayProcessId);
    using var overlayProcess = Process.GetProcessById(checked((int)overlayProcessId));
    using var telemetryHostProcess = Process.GetCurrentProcess();
    _ = NativeMethods.SetForegroundWindow(probe.MainWindowHandle);
    await Task.Delay(100);
    var didNotStealFocus = NativeMethods.GetForegroundWindow() != overlayWindow;
    var requiredStyles = 0x00000008L | 0x00000020L | 0x00000080L | 0x00080000L | 0x08000000L;
    var stylesAreSafe =
        (NativeMethods.GetWindowLongPtrW(overlayWindow, -20).ToInt64() & requiredStyles) == requiredStyles;

    telemetryHostProcess.Refresh();
    overlayProcess.Refresh();
    var cpuStart = telemetryHostProcess.TotalProcessorTime + overlayProcess.TotalProcessorTime;
    var sampleClock = Stopwatch.StartNew();
    var relocation = TimeSpan.Zero;
    var moved = false;
    var relocationClock = new Stopwatch();
    var previousBounds = initialWindow.ContentBounds;
    for (var tick = 0; tick < 16; tick++)
    {
        if (!borderless && tick == 2)
        {
            relocationClock.Start();
            _ = NativeMethods.MoveWindow(probe.MainWindowHandle, 180, 140, 840, 520, true);
            moved = true;
        }

        var window = await locator.FindPrimaryWindowAsync(runtime, CancellationToken.None);
        if (window is not null)
        {
            if (moved && relocation == TimeSpan.Zero && window.ContentBounds != previousBounds)
            {
                relocationClock.Stop();
                relocation = relocationClock.Elapsed;
            }

            var snapshot = telemetry.Current ?? CreateUnavailableSnapshot(gameId);
            await overlay.UpdateAsync(new OverlayFrame(window, profile, snapshot, true), CancellationToken.None);
        }

        await Task.Delay(250);
    }

    sampleClock.Stop();
    telemetryHostProcess.Refresh();
    overlayProcess.Refresh();
    var cpuDelta = telemetryHostProcess.TotalProcessorTime + overlayProcess.TotalProcessorTime - cpuStart;
    var normalizedCpu = cpuDelta.TotalMilliseconds /
                        (sampleClock.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100d;
    var overlayWorkingSetMb = overlayProcess.WorkingSet64 / 1024d / 1024d;
    var snapshotAtEnd = telemetry.Current ?? CreateUnavailableSnapshot(gameId);
    var noResidualBeforeShutdown = !probe.HasExited && !overlayProcess.HasExited;

    Console.WriteLine($"API 测试程序：{Path.GetFileName(probePath)}");
    Console.WriteLine($"模式：{(borderless ? "无边框全屏" : "窗口化")}");
    Console.WriteLine($"窗口定位：通过，DPI={initialWindow.Dpi}，全屏覆盖={initialWindow.CoversMonitor}");
    Console.WriteLine($"覆盖层样式：{(stylesAreSafe ? "通过" : "失败")}（置顶/穿透/工具窗/分层/不激活）");
    Console.WriteLine($"不抢焦点：{(didNotStealFocus ? "通过" : "失败")}");
    Console.WriteLine($"重新定位：{(borderless ? "不适用" : $"{relocation.TotalMilliseconds:0} ms")}");
    Console.WriteLine($"FPS：{snapshotAtEnd.Fps.Status}；能力检测：{capability.Fps.Status}");
    Console.WriteLine($"CPU：{FormatMetric(snapshotAtEnd.CpuPercent, "%")}");
    Console.WriteLine($"GPU：{FormatMetric(snapshotAtEnd.GpuPercent, "%")}");
    Console.WriteLine($"RAM：{FormatMetric(snapshotAtEnd.RamBytes, " bytes")}");
    Console.WriteLine($"遥测宿主 + Overlay 归一化 CPU：{normalizedCpu:0.00}%（预算 < 2%）");
    Console.WriteLine($"Overlay 工作集：{overlayWorkingSetMb:0.0} MB（预算 < 80 MB）");

    var relocationPassed = borderless || relocation is { TotalMilliseconds: > 0 and < 1000 };
    var performancePassed = normalizedCpu < 2 && overlayWorkingSetMb < 80;
    return stylesAreSafe && didNotStealFocus && relocationPassed && performancePassed && noResidualBeforeShutdown
        ? 0
        : 7;
}
finally
{
    if (overlay is not null)
    {
        await overlay.ShutdownAsync(CancellationToken.None);
        await overlay.DisposeAsync();
    }

    if (telemetry is not null)
    {
        await telemetry.StopAsync(CancellationToken.None);
        await telemetry.DisposeAsync();
    }

    if (!probe.HasExited)
    {
        _ = probe.CloseMainWindow();
        if (!probe.WaitForExit(3000))
        {
            probe.Kill(entireProcessTree: true);
            await probe.WaitForExitAsync();
        }
    }

    var residualOverlay = Process.GetProcessesByName("GameNest.Overlay")
        .Where(process => process.StartTime.ToUniversalTime() >= startedAtUtc.UtcDateTime)
        .ToArray();
    foreach (var process in residualOverlay)
    {
        process.Dispose();
    }
}

static PerformanceSnapshot CreateUnavailableSnapshot(Guid gameId) =>
    new(
        gameId,
        DateTimeOffset.UtcNow,
        TelemetryMetric.Starting(),
        TelemetryMetric.Starting(),
        TelemetryMetric.Starting(),
        TelemetryMetric.Starting());

static string FormatMetric(TelemetryMetric metric, string suffix) =>
    metric.Value is null ? metric.Status.ToString() : $"{metric.Value:0.0}{suffix}";

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern nint SetProcessDpiAwarenessContext(nint value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindowW(string className, string windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveWindow(
        nint window,
        int left,
        int top,
        int width,
        int height,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);
}
