using System.Collections.Concurrent;
using GameNest.Application;
using GameNest.Domain;
using Microsoft.Extensions.Logging;

namespace GameNest.Infrastructure.Windows;

public sealed class WindowsGameRuntimeService(
    IProcessSnapshotProvider snapshotProvider,
    IGameProcessController processController,
    IGameRuntimeRepository runtimeRepository,
    GameRuntimeOptions options,
    ILogger<WindowsGameRuntimeService> logger) : IGameLaunchService, IAsyncDisposable
{
    private static readonly TimeSpan SteamLauncherAdoptionWindow = TimeSpan.FromSeconds(30);

    private static readonly Action<ILogger, string, int, Exception?> GameStarted =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(1200, nameof(GameStarted)),
            "已启动游戏 {GameTitle}，入口 PID 为 {ProcessId}。");

    private static readonly Action<ILogger, Guid, int, string, Exception?> ProcessAdopted =
        LoggerMessage.Define<Guid, int, string>(
            LogLevel.Information,
            new EventId(1201, nameof(ProcessAdopted)),
            "游戏 {GameId} 接管进程 {ProcessId}，置信度为 {Confidence}。");

    private static readonly Action<ILogger, Guid, string, Exception?> SessionCompleted =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(1202, nameof(SessionCompleted)),
            "游戏 {GameId} 会话已结束，退出类型为 {ExitKind}。");

    private static readonly Action<ILogger, Guid, Exception?> MonitorFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(1203, nameof(MonitorFailed)),
            "游戏 {GameId} 的进程监视发生错误。");

    private static readonly Action<ILogger, Guid, Exception?> SessionPersistenceFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(1204, nameof(SessionPersistenceFailed)),
            "游戏 {GameId} 的会话结算写入失败。");

    private readonly ConcurrentDictionary<Guid, RuntimeRegistration> _runtimes = new();
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public event EventHandler<GameProcessStatusChangedEventArgs>? StatusChanged;

    public bool IsRunning(Guid gameId) => GetRuntime(gameId)?.IsRunning == true;

    public GameRuntimeSnapshot? GetRuntime(Guid gameId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _runtimes.TryGetValue(gameId, out var registration)
            ? CreateSnapshot(registration)
            : null;
    }

    public async Task<GameLaunchResult> LaunchAsync(Game game, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(game);
        await _launchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning(game.Id))
            {
                throw new InvalidOperationException("该游戏已经在运行，请勿重复启动。");
            }

            Publish(
                new GameRuntimeSnapshot(
                    game.Id,
                    GameRuntimeState.Launching,
                    null,
                    GameProcessConfidence.Unconfirmed,
                    null,
                    []));

            var before = await snapshotProvider.CaptureAsync(cancellationToken).ConfigureAwait(false);
            var started = await processController.StartAsync(game, cancellationToken).ConfigureAwait(false);
            var startedAtUtc = DateTimeOffset.UtcNow;
            var directProcess = new TrackedGameProcess(
                started.ProcessId,
                null,
                Path.GetFileNameWithoutExtension(game.LaunchProfile.ExecutablePath),
                game.LaunchProfile.ExecutablePath,
                started.StartTimeUtc,
                GameProcessConfidence.Confirmed);
            var session = new PlaySession(
                Guid.NewGuid(),
                game.Id,
                startedAtUtc,
                null,
                null,
                null,
                [started.ProcessId]);
            await runtimeRepository.StartSessionAsync(session, cancellationToken).ConfigureAwait(false);

            var registration = new RuntimeRegistration(game, session, before.Processes.Keys, directProcess);
            if (!_runtimes.TryAdd(game.Id, registration))
            {
                await runtimeRepository
                    .CompleteSessionAsync(
                        session.Id,
                        DateTimeOffset.UtcNow,
                        GameExitKind.TrackingLost,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                throw new InvalidOperationException("该游戏的启动状态正在更新，请稍后重试。");
            }

            GameStarted(logger, game.Title, started.ProcessId, null);
            Publish(CreateSnapshot(registration));
            registration.MonitorTask = MonitorAsync(registration, _lifetime.Token);
            return new GameLaunchResult(
                game.Id,
                started.ProcessId,
                GameRuntimeState.Running,
                GameProcessConfidence.Confirmed);
        }
        catch
        {
            if (!_runtimes.ContainsKey(game.Id))
            {
                Publish(
                    new GameRuntimeSnapshot(
                        game.Id,
                        GameRuntimeState.NotRunning,
                        null,
                        GameProcessConfidence.Unconfirmed,
                        null,
                        []));
            }

            throw;
        }
        finally
        {
            _launchGate.Release();
        }
    }

    public async Task<GameStopResult> StopAsync(
        Guid gameId,
        bool force,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_runtimes.TryGetValue(gameId, out var registration))
        {
            return new GameStopResult(gameId, GameStopOutcome.AlreadyStopped, [], "游戏已经停止。");
        }

        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var confirmed = GetLiveConfirmedProcesses(registration);
            if (confirmed.Length == 0)
            {
                return new GameStopResult(
                    gameId,
                    GameStopOutcome.UnsafeTarget,
                    GetLiveProcesses(registration).Select(static process => process.ProcessId).ToArray(),
                    "当前只找到未确认的候选进程。为避免误伤，GameNest 不提供停止操作。");
            }

            SetState(registration, GameRuntimeState.Stopping);
            if (force)
            {
                registration.RequestedExitKind = GameExitKind.Forced;
                foreach (var process in confirmed)
                {
                    await processController
                        .KillAsync(process.ProcessId, process.StartTimeUtc, cancellationToken)
                        .ConfigureAwait(false);
                }

                var remaining = await WaitForExitAsync(
                        confirmed,
                        options.ForceStopWaitTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (remaining.Count == 0)
                {
                    await CompleteAsync(registration, GameExitKind.Forced).ConfigureAwait(false);
                    return new GameStopResult(gameId, GameStopOutcome.Stopped, [], "游戏进程已强制结束。");
                }

                SetState(registration, GameRuntimeState.Running);
                return new GameStopResult(
                    gameId,
                    GameStopOutcome.ConfirmationRequired,
                    remaining,
                    "部分进程仍在运行，请检查权限或反作弊保护。");
            }

            var closeRequested = false;
            foreach (var process in confirmed)
            {
                closeRequested |= await processController
                    .TryCloseMainWindowAsync(process.ProcessId, process.StartTimeUtc, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (closeRequested)
            {
                registration.RequestedExitKind = GameExitKind.Graceful;
                var remaining = await WaitForExitAsync(
                        confirmed,
                        TimeSpan.FromSeconds(registration.Game.LaunchProfile.GracefulStopTimeoutSeconds),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (remaining.Count == 0)
                {
                    await CompleteAsync(registration, GameExitKind.Graceful).ConfigureAwait(false);
                    return new GameStopResult(gameId, GameStopOutcome.Stopped, [], "游戏已正常关闭。");
                }
            }

            registration.RequestedExitKind = null;
            SetState(registration, GameRuntimeState.Running);
            var liveProcessIds = GetLiveConfirmedProcesses(registration)
                .Select(static process => process.ProcessId)
                .ToArray();
            return new GameStopResult(
                gameId,
                GameStopOutcome.ConfirmationRequired,
                liveProcessIds,
                closeRequested
                    ? "游戏在等待时间内没有退出。强制结束可能造成未保存进度丢失。"
                    : "游戏没有可用的主窗口。强制结束可能造成未保存进度丢失。");
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        var registrations = _runtimes.Values.ToArray();
        foreach (var registration in registrations)
        {
            await CompleteAsync(registration, GameExitKind.ApplicationClosed).ConfigureAwait(false);
        }

        var monitorTasks = registrations
            .Select(static registration => registration.MonitorTask)
            .Where(static task => task is not null)
            .Cast<Task>()
            .ToArray();
        try
        {
            await Task.WhenAll(monitorTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _launchGate.Dispose();
        _lifetime.Dispose();
    }

    private async Task MonitorAsync(RuntimeRegistration registration, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _runtimes.ContainsKey(registration.Game.Id))
            {
                await Task.Delay(options.MonitorInterval, cancellationToken).ConfigureAwait(false);
                var current = await snapshotProvider.CaptureAsync(cancellationToken).ConfigureAwait(false);
                var adopted = AdoptProcesses(registration, current);
                if (adopted.Count > 0)
                {
                    await runtimeRepository
                        .UpdateTrackedProcessIdsAsync(
                            registration.Session.Id,
                            registration.LineageProcessIds,
                            cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var process in adopted)
                    {
                        ProcessAdopted(
                            logger,
                            registration.Game.Id,
                            process.ProcessId,
                            process.Confidence.ToString(),
                            null);
                    }
                }

                UpdateLiveProcesses(registration, current);
                if (adopted.Count > 0)
                {
                    Publish(CreateSnapshot(registration));
                }

                if (GetLiveProcesses(registration).Length > 0)
                {
                    registration.LastProcessSeenAtUtc = DateTimeOffset.UtcNow;
                    if (registration.State != GameRuntimeState.Stopping)
                    {
                        SetState(registration, GameRuntimeState.Running);
                    }

                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                if (now - registration.Session.StartedAtUtc < GetLauncherAdoptionWindow(registration.Game) ||
                    now - registration.LastProcessSeenAtUtc < options.EmptyProcessGracePeriod)
                {
                    continue;
                }

                await CompleteAsync(
                        registration,
                        registration.RequestedExitKind ?? GameExitKind.Natural)
                    .ConfigureAwait(false);
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            MonitorFailed(logger, registration.Game.Id, exception);
            await CompleteAsync(registration, GameExitKind.TrackingLost).ConfigureAwait(false);
        }
    }

    private static List<TrackedGameProcess> AdoptProcesses(
        RuntimeRegistration registration,
        ProcessSnapshot current)
    {
        var adopted = new List<TrackedGameProcess>();
        lock (registration.SyncRoot)
        {
            foreach (var candidate in current.Processes.Values)
            {
                if (registration.PreLaunchProcessIds.Contains(candidate.ProcessId) ||
                    registration.LineageProcessIds.Contains(candidate.ProcessId))
                {
                    continue;
                }

                var confidence = ClassifyCandidate(registration, candidate, current);
                if (confidence == GameProcessConfidence.Unconfirmed)
                {
                    continue;
                }

                var process = new TrackedGameProcess(
                    candidate.ProcessId,
                    candidate.ParentProcessId,
                    candidate.ProcessName,
                    candidate.ExecutablePath,
                    candidate.StartTimeUtc,
                    confidence);
                registration.Processes[candidate.ProcessId] = process;
                registration.LineageProcessIds.Add(candidate.ProcessId);
                adopted.Add(process);
            }
        }

        return adopted;
    }

    private static GameProcessConfidence ClassifyCandidate(
        RuntimeRegistration registration,
        ProcessSnapshotEntry candidate,
        ProcessSnapshot current)
    {
        if (PathEquals(candidate.ExecutablePath, registration.Game.LaunchProfile.ExecutablePath))
        {
            return GameProcessConfidence.Confirmed;
        }

        if (registration.Game.LaunchProfile.ExpectedProcessNames.Contains(
                candidate.ProcessName,
                StringComparer.OrdinalIgnoreCase))
        {
            return GameProcessConfidence.Confirmed;
        }

        if (IsAuxiliaryProcess(candidate.ProcessName))
        {
            return GameProcessConfidence.Unconfirmed;
        }

        var isInsideInstallRoot = IsPathInside(candidate.ExecutablePath, registration.Game.InstallRoot);
        if (!isInsideInstallRoot)
        {
            return GameProcessConfidence.Unconfirmed;
        }

        // Steam 可能由平台进程接管启动，实际游戏进程不再保留原有父子关系。
        // 平台清单来源与安装目录边界共同作为归属依据。
        if (registration.Game.SourceType == GameSourceType.Steam ||
            IsDescendantOfTrackedProcess(candidate, current, registration.LineageProcessIds) ||
            IsDescendantOfSteamClient(candidate, current))
        {
            return GameProcessConfidence.Confirmed;
        }

        return GameProcessConfidence.Probable;
    }

    private TimeSpan GetLauncherAdoptionWindow(Game game) =>
        IsSteamInstallRoot(game)
            ? options.LauncherAdoptionWindow > SteamLauncherAdoptionWindow
                ? options.LauncherAdoptionWindow
                : SteamLauncherAdoptionWindow
            : options.LauncherAdoptionWindow;

    private static bool IsDescendantOfTrackedProcess(
        ProcessSnapshotEntry candidate,
        ProcessSnapshot current,
        HashSet<int> trackedProcessIds)
    {
        var parentId = candidate.ParentProcessId;
        var visited = new HashSet<int>();
        for (var depth = 0; parentId is not null && depth < 32 && visited.Add(parentId.Value); depth++)
        {
            if (trackedProcessIds.Contains(parentId.Value))
            {
                return true;
            }

            parentId = current.Processes.TryGetValue(parentId.Value, out var parent)
                ? parent.ParentProcessId
                : null;
        }

        return false;
    }

    private static bool IsDescendantOfSteamClient(
        ProcessSnapshotEntry candidate,
        ProcessSnapshot current)
    {
        var parentId = candidate.ParentProcessId;
        var visited = new HashSet<int>();
        for (var depth = 0; parentId is not null && depth < 32 && visited.Add(parentId.Value); depth++)
        {
            if (!current.Processes.TryGetValue(parentId.Value, out var parent))
            {
                return false;
            }

            if (string.Equals(parent.ProcessName, "steam", StringComparison.OrdinalIgnoreCase) &&
                parent.ExecutablePath?.EndsWith("\\steam.exe", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            parentId = parent.ParentProcessId;
        }

        return false;
    }

    private static bool IsSteamInstallRoot(Game game) =>
        game.SourceType == GameSourceType.Steam ||
        game.InstallRoot.Contains("\\steamapps\\common\\", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuxiliaryProcess(string processName) =>
        processName.Contains("crashhandler", StringComparison.OrdinalIgnoreCase) ||
        processName.Contains("crashreport", StringComparison.OrdinalIgnoreCase) ||
        processName.Contains("crashpad", StringComparison.OrdinalIgnoreCase);

    private static bool PathEquals(string? left, string right) =>
        !string.IsNullOrWhiteSpace(left) &&
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsPathInside(string? candidatePath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath)) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(candidatePath);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void UpdateLiveProcesses(RuntimeRegistration registration, ProcessSnapshot current)
    {
        lock (registration.SyncRoot)
        {
            registration.LiveProcessIds.Clear();
            foreach (var processId in registration.LineageProcessIds)
            {
                if (!current.Processes.TryGetValue(processId, out var currentProcess))
                {
                    continue;
                }

                if (registration.Processes.TryGetValue(processId, out var tracked) &&
                    IsSameProcess(tracked.StartTimeUtc, currentProcess.StartTimeUtc))
                {
                    registration.LiveProcessIds.Add(processId);
                }
            }
        }
    }

    private async Task<IReadOnlyList<int>> WaitForExitAsync(
        IReadOnlyList<TrackedGameProcess> processes,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            var remaining = new List<int>();
            foreach (var process in processes)
            {
                if (await processController
                        .IsAliveAsync(process.ProcessId, process.StartTimeUtc, cancellationToken)
                        .ConfigureAwait(false))
                {
                    remaining.Add(process.ProcessId);
                }
            }

            if (remaining.Count == 0 || DateTimeOffset.UtcNow >= deadline)
            {
                return remaining;
            }

            await Task.Delay(options.MonitorInterval, cancellationToken).ConfigureAwait(false);
        }
        while (true);
    }

    private async Task CompleteAsync(RuntimeRegistration registration, GameExitKind exitKind)
    {
        if (!_runtimes.TryRemove(new KeyValuePair<Guid, RuntimeRegistration>(registration.Game.Id, registration)))
        {
            return;
        }

        try
        {
            await runtimeRepository
                .CompleteSessionAsync(
                    registration.Session.Id,
                    DateTimeOffset.UtcNow,
                    exitKind,
                    CancellationToken.None)
                .ConfigureAwait(false);
            SessionCompleted(logger, registration.Game.Id, exitKind.ToString(), null);
        }
        catch (Exception exception)
        {
            SessionPersistenceFailed(logger, registration.Game.Id, exception);
        }
        finally
        {
            Publish(
                new GameRuntimeSnapshot(
                    registration.Game.Id,
                    GameRuntimeState.NotRunning,
                    null,
                    GameProcessConfidence.Unconfirmed,
                    null,
                    []));
        }
    }

    private static TrackedGameProcess[] GetLiveProcesses(RuntimeRegistration registration)
    {
        lock (registration.SyncRoot)
        {
            return registration.LiveProcessIds
                .Select(processId => registration.Processes[processId])
                .OrderBy(static process => process.ProcessId)
                .ToArray();
        }
    }

    private static TrackedGameProcess[] GetLiveConfirmedProcesses(RuntimeRegistration registration) =>
        GetLiveProcesses(registration)
            .Where(
                static process =>
                    process.Confidence == GameProcessConfidence.Confirmed &&
                    process.StartTimeUtc is not null)
            .ToArray();

    private static GameRuntimeSnapshot CreateSnapshot(RuntimeRegistration registration)
    {
        var processes = GetLiveProcesses(registration);
        var primary = processes
            .OrderByDescending(static process => process.Confidence)
            .ThenByDescending(process => process.ProcessId != registration.DirectProcessId)
            .FirstOrDefault();
        return new GameRuntimeSnapshot(
            registration.Game.Id,
            registration.State,
            primary?.ProcessId,
            primary?.Confidence ?? GameProcessConfidence.Unconfirmed,
            registration.Session.StartedAtUtc,
            processes);
    }

    private void SetState(RuntimeRegistration registration, GameRuntimeState state)
    {
        var changed = false;
        lock (registration.SyncRoot)
        {
            if (registration.State != state)
            {
                registration.State = state;
                changed = true;
            }
        }

        if (changed)
        {
            Publish(CreateSnapshot(registration));
        }
    }

    private void Publish(GameRuntimeSnapshot runtime) =>
        StatusChanged?.Invoke(this, new GameProcessStatusChangedEventArgs(runtime));

    private static bool IsSameProcess(DateTimeOffset? expected, DateTimeOffset? actual) =>
        expected is null || actual is null || Math.Abs((expected.Value - actual.Value).TotalSeconds) <= 1;

    private sealed class RuntimeRegistration
    {
        public RuntimeRegistration(
            Game game,
            PlaySession session,
            IEnumerable<int> preLaunchProcessIds,
            TrackedGameProcess directProcess)
        {
            Game = game;
            Session = session;
            PreLaunchProcessIds = preLaunchProcessIds.ToHashSet();
            Processes[directProcess.ProcessId] = directProcess;
            LineageProcessIds.Add(directProcess.ProcessId);
            LiveProcessIds.Add(directProcess.ProcessId);
            DirectProcessId = directProcess.ProcessId;
            LastProcessSeenAtUtc = session.StartedAtUtc;
        }

        public object SyncRoot { get; } = new();

        public SemaphoreSlim OperationGate { get; } = new(1, 1);

        public Game Game { get; }

        public PlaySession Session { get; }

        public HashSet<int> PreLaunchProcessIds { get; }

        public Dictionary<int, TrackedGameProcess> Processes { get; } = [];

        public HashSet<int> LineageProcessIds { get; } = [];

        public HashSet<int> LiveProcessIds { get; } = [];

        public int DirectProcessId { get; }

        public GameRuntimeState State { get; set; } = GameRuntimeState.Running;

        public DateTimeOffset LastProcessSeenAtUtc { get; set; }

        public GameExitKind? RequestedExitKind { get; set; }

        public Task? MonitorTask { get; set; }
    }
}
