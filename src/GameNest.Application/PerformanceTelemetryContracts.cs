using GameNest.Domain;

namespace GameNest.Application;

public enum TelemetryMetricStatus
{
    Starting,
    Available,
    Unavailable,
    PermissionDenied,
    NotSupported,
    TargetExited,
}

public sealed record TelemetryMetric(
    double? Value,
    TelemetryMetricStatus Status,
    string? Detail = null)
{
    public static TelemetryMetric Starting(string? detail = null) =>
        new(null, TelemetryMetricStatus.Starting, detail);

    public static TelemetryMetric Available(double value) =>
        new(value, TelemetryMetricStatus.Available);

    public static TelemetryMetric Unavailable(
        string detail,
        TelemetryMetricStatus status = TelemetryMetricStatus.Unavailable) =>
        new(null, status, detail);
}

public sealed record PerformanceSnapshot(
    Guid GameId,
    DateTimeOffset CapturedAtUtc,
    TelemetryMetric Fps,
    TelemetryMetric CpuPercent,
    TelemetryMetric GpuPercent,
    TelemetryMetric RamBytes);

public sealed class PerformanceSnapshotEventArgs(PerformanceSnapshot snapshot) : EventArgs
{
    public PerformanceSnapshot Snapshot { get; } = snapshot;
}

public sealed record TelemetryTarget(
    Guid GameId,
    int PrimaryProcessId,
    IReadOnlyList<int> ConfirmedProcessIds);

public sealed record TelemetryCapability(
    TelemetryMetricStatus Status,
    string Message);

public sealed record TelemetryCapabilityReport(
    TelemetryCapability Fps,
    TelemetryCapability Cpu,
    TelemetryCapability Gpu,
    TelemetryCapability Ram,
    string PresentMonVersion,
    string PresentMonPath);

public interface IPerformanceTelemetry
{
    event EventHandler<PerformanceSnapshotEventArgs>? SnapshotAvailable;

    PerformanceSnapshot? Current { get; }

    Task<TelemetryCapabilityReport> CheckCapabilityAsync(CancellationToken cancellationToken);

    Task StartAsync(TelemetryTarget target, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public sealed record GameWindowBounds(int Left, int Top, int Width, int Height);

public sealed record GameWindowSnapshot(
    long WindowHandle,
    GameWindowBounds ContentBounds,
    uint Dpi,
    bool IsForeground,
    bool IsMinimized,
    bool CoversMonitor);

public interface IGameWindowLocator
{
    Task<GameWindowSnapshot?> FindPrimaryWindowAsync(
        GameRuntimeSnapshot runtime,
        CancellationToken cancellationToken);
}

public sealed record OverlayFrame(
    GameWindowSnapshot Window,
    OverlayProfile Profile,
    PerformanceSnapshot Snapshot,
    bool IsVisible);

public enum OverlayControllerState
{
    Stopped,
    Starting,
    Ready,
    Disconnected,
    Faulted,
}

public sealed record OverlayControllerStatus(
    OverlayControllerState State,
    bool IsHotkeyAvailable,
    string Message);

public sealed class OverlayControllerStatusEventArgs(OverlayControllerStatus status) : EventArgs
{
    public OverlayControllerStatus Status { get; } = status;
}

public interface IOverlayController
{
    event EventHandler<OverlayControllerStatusEventArgs>? StatusChanged;

    OverlayControllerStatus Status { get; }

    Task EnsureStartedAsync(CancellationToken cancellationToken);

    Task UpdateAsync(OverlayFrame frame, CancellationToken cancellationToken);

    Task HideAsync(CancellationToken cancellationToken);

    Task ShutdownAsync(CancellationToken cancellationToken);

    Task<bool> IsHotkeyAvailableAsync(OverlayHotkey hotkey, CancellationToken cancellationToken);
}
