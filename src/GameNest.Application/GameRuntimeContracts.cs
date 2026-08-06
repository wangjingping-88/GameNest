using GameNest.Domain;

namespace GameNest.Application;

public sealed record ProcessSnapshotEntry(
    int ProcessId,
    int? ParentProcessId,
    string ProcessName,
    string? ExecutablePath,
    DateTimeOffset? StartTimeUtc);

public sealed record ProcessSnapshot(IReadOnlyDictionary<int, ProcessSnapshotEntry> Processes)
{
    public static ProcessSnapshot Empty { get; } = new(new Dictionary<int, ProcessSnapshotEntry>());
}

public sealed record StartedProcess(int ProcessId, DateTimeOffset? StartTimeUtc);

public interface IProcessSnapshotProvider
{
    Task<ProcessSnapshot> CaptureAsync(CancellationToken cancellationToken);
}

public interface IGameProcessController
{
    Task<StartedProcess> StartAsync(Game game, CancellationToken cancellationToken);

    Task<bool> IsAliveAsync(
        int processId,
        DateTimeOffset? expectedStartTimeUtc,
        CancellationToken cancellationToken);

    Task<bool> TryCloseMainWindowAsync(
        int processId,
        DateTimeOffset? expectedStartTimeUtc,
        CancellationToken cancellationToken);

    Task KillAsync(
        int processId,
        DateTimeOffset? expectedStartTimeUtc,
        CancellationToken cancellationToken);
}

public sealed record TrackedGameProcess(
    int ProcessId,
    int? ParentProcessId,
    string ProcessName,
    string? ExecutablePath,
    DateTimeOffset? StartTimeUtc,
    GameProcessConfidence Confidence);

public sealed record GameRuntimeSnapshot(
    Guid GameId,
    GameRuntimeState State,
    int? PrimaryProcessId,
    GameProcessConfidence Confidence,
    DateTimeOffset? SessionStartedAtUtc,
    IReadOnlyList<TrackedGameProcess> Processes)
{
    public bool IsRunning => State is GameRuntimeState.Launching or GameRuntimeState.Running or GameRuntimeState.Stopping;

    public bool CanStop =>
        State == GameRuntimeState.Running &&
        Processes.Any(
            static process =>
                process.Confidence == GameProcessConfidence.Confirmed &&
                process.StartTimeUtc is not null);
}

public enum GameStopOutcome
{
    AlreadyStopped,
    Stopped,
    ConfirmationRequired,
    UnsafeTarget,
}

public sealed record GameStopResult(
    Guid GameId,
    GameStopOutcome Outcome,
    IReadOnlyList<int> RemainingProcessIds,
    string Message);

public sealed record GameRuntimeOptions(
    TimeSpan MonitorInterval,
    TimeSpan LauncherAdoptionWindow,
    TimeSpan EmptyProcessGracePeriod,
    TimeSpan ForceStopWaitTimeout)
{
    public static GameRuntimeOptions Default { get; } = new(
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromMilliseconds(900),
        TimeSpan.FromSeconds(5));
}
