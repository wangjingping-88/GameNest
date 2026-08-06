using System.Threading.Channels;
using GameNest.Domain;

namespace GameNest.Application;

public enum OverlayRuntimeState
{
    Stopped,
    Disabled,
    WaitingForWindow,
    Active,
    Hidden,
    Unavailable,
}

public sealed record OverlayRuntimeStatus(
    OverlayRuntimeState State,
    Guid? GameId,
    string Message,
    bool CoversMonitor = false);

public sealed class OverlayRuntimeStatusEventArgs(OverlayRuntimeStatus status) : EventArgs
{
    public OverlayRuntimeStatus Status { get; } = status;
}

public interface IOverlayRuntimeCoordinator
{
    event EventHandler<OverlayRuntimeStatusEventArgs>? StatusChanged;

    OverlayRuntimeStatus Status { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task RefreshProfileAsync(CancellationToken cancellationToken);
}

public sealed class OverlayRuntimeCoordinator : IOverlayRuntimeCoordinator, IAsyncDisposable
{
    private readonly GameLibraryService _gameLibraryService;
    private readonly OverlaySettingsService _settingsService;
    private readonly IPerformanceTelemetry _performanceTelemetry;
    private readonly IGameWindowLocator _windowLocator;
    private readonly IOverlayController _overlayController;
    private readonly Channel<GameRuntimeSnapshot> _runtimeChanges = Channel.CreateBounded<GameRuntimeSnapshot>(
        new BoundedChannelOptions(16)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _trackingLifetime;
    private Task? _trackingTask;
    private Task? _workerTask;
    private GameRuntimeSnapshot? _runtime;
    private OverlayProfile? _profile;
    private OverlayRuntimeStatus _status =
        new(OverlayRuntimeState.Stopped, null, "当前没有覆盖层会话。");
    private bool _initialized;
    private bool _disposed;

    public OverlayRuntimeCoordinator(
        GameLibraryService gameLibraryService,
        OverlaySettingsService settingsService,
        IPerformanceTelemetry performanceTelemetry,
        IGameWindowLocator windowLocator,
        IOverlayController overlayController)
    {
        _gameLibraryService = gameLibraryService;
        _settingsService = settingsService;
        _performanceTelemetry = performanceTelemetry;
        _windowLocator = windowLocator;
        _overlayController = overlayController;
    }

    public event EventHandler<OverlayRuntimeStatusEventArgs>? StatusChanged;

    public OverlayRuntimeStatus Status
    {
        get
        {
            lock (_syncRoot)
            {
                return _status;
            }
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        _initialized = true;
        _gameLibraryService.RuntimeStatusChanged += HandleRuntimeStatusChanged;
        _workerTask = ProcessRuntimeChangesAsync(_lifetime.Token);
        return Task.CompletedTask;
    }

    public async Task RefreshProfileAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GameRuntimeSnapshot? runtime;
        lock (_syncRoot)
        {
            runtime = _runtime;
        }

        if (runtime?.IsRunning == true)
        {
            await ConfigureSessionAsync(runtime, forceRestart: true, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gameLibraryService.RuntimeStatusChanged -= HandleRuntimeStatusChanged;
        _runtimeChanges.Writer.TryComplete();
        _lifetime.Cancel();
        await _sessionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StopSessionCoreAsync(CancellationToken.None).ConfigureAwait(false);
            await _overlayController.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
        }

        if (_workerTask is not null)
        {
            try
            {
                await _workerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _sessionGate.Dispose();
        _lifetime.Dispose();
    }

    private void HandleRuntimeStatusChanged(object? sender, GameProcessStatusChangedEventArgs args)
    {
        _ = sender;
        _runtimeChanges.Writer.TryWrite(args.Runtime);
    }

    private async Task ProcessRuntimeChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var runtime in _runtimeChanges.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!runtime.IsRunning)
                {
                    await StopSessionAsync(runtime.GameId, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (runtime.State == GameRuntimeState.Running &&
                    runtime.Confidence == GameProcessConfidence.Confirmed &&
                    runtime.PrimaryProcessId is not null)
                {
                    await ConfigureSessionAsync(runtime, forceRestart: false, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ConfigureSessionAsync(
        GameRuntimeSnapshot runtime,
        bool forceRestart,
        CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GameRuntimeSnapshot? current;
            lock (_syncRoot)
            {
                current = _runtime;
            }

            if (!forceRestart &&
                current?.GameId == runtime.GameId &&
                current.PrimaryProcessId == runtime.PrimaryProcessId &&
                SameConfirmedProcesses(current, runtime))
            {
                lock (_syncRoot)
                {
                    _runtime = runtime;
                }

                return;
            }

            await StopSessionCoreAsync(CancellationToken.None).ConfigureAwait(false);
            var profile = await _settingsService
                .GetResolvedAsync(runtime.GameId, cancellationToken)
                .ConfigureAwait(false);
            lock (_syncRoot)
            {
                _runtime = runtime;
                _profile = profile;
            }

            if (!profile.IsEnabled)
            {
                SetStatus(
                    new OverlayRuntimeStatus(
                        OverlayRuntimeState.Disabled,
                        runtime.GameId,
                        "该游戏的性能覆盖层已关闭。"));
                return;
            }

            var target = CreateTelemetryTarget(runtime);
            var trackingLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            await _performanceTelemetry.StartAsync(target, cancellationToken).ConfigureAwait(false);
            lock (_syncRoot)
            {
                _trackingLifetime = trackingLifetime;
                _trackingTask = TrackWindowAsync(runtime.GameId, trackingLifetime.Token);
            }

            SetStatus(
                new OverlayRuntimeStatus(
                    OverlayRuntimeState.WaitingForWindow,
                    runtime.GameId,
                    "正在查找游戏主窗口。"));
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task TrackWindowAsync(Guid gameId, CancellationToken cancellationToken)
    {
        int? telemetryPrimaryProcessId = null;
        int overlayFailureCount = 0;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var runtime = _gameLibraryService.GetRuntime(gameId);
                if (runtime?.IsRunning != true || runtime.PrimaryProcessId is null)
                {
                    return;
                }

                OverlayProfile? profile;
                lock (_syncRoot)
                {
                    _runtime = runtime;
                    profile = _profile;
                }

                if (profile is null || !profile.IsEnabled)
                {
                    await _overlayController.HideAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (telemetryPrimaryProcessId is null)
                {
                    telemetryPrimaryProcessId = runtime.PrimaryProcessId;
                }
                else if (telemetryPrimaryProcessId != runtime.PrimaryProcessId)
                {
                    await _performanceTelemetry
                        .StartAsync(CreateTelemetryTarget(runtime), cancellationToken)
                        .ConfigureAwait(false);
                    telemetryPrimaryProcessId = runtime.PrimaryProcessId;
                }

                var window = await _windowLocator
                    .FindPrimaryWindowAsync(runtime, cancellationToken)
                    .ConfigureAwait(false);
                var snapshot = _performanceTelemetry.Current;
                if (window is null || snapshot is null)
                {
                    await _overlayController.HideAsync(cancellationToken).ConfigureAwait(false);
                    SetStatus(
                        new OverlayRuntimeStatus(
                            OverlayRuntimeState.WaitingForWindow,
                            gameId,
                            window is null ? "尚未找到可显示的游戏窗口。" : "正在等待首个性能快照。"));
                    continue;
                }

                var visible = !window.IsMinimized &&
                              (!profile.HideWhenGameNotForeground || window.IsForeground);
                try
                {
                    await _overlayController
                        .UpdateAsync(new OverlayFrame(window, profile, snapshot, visible), cancellationToken)
                        .ConfigureAwait(false);
                    overlayFailureCount = 0;
                    SetStatus(
                        new OverlayRuntimeStatus(
                            visible ? OverlayRuntimeState.Active : OverlayRuntimeState.Hidden,
                            gameId,
                            visible
                                ? window.CoversMonitor
                                    ? "覆盖层已显示。全屏模式仅保证无边框兼容；若不可见请切换为无边框窗口。"
                                    : "覆盖层已显示。"
                                : "游戏窗口不在前台或已最小化，覆盖层已自动隐藏。",
                            window.CoversMonitor));
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    overlayFailureCount++;
                    SetStatus(
                        new OverlayRuntimeStatus(
                            OverlayRuntimeState.Unavailable,
                            gameId,
                            "覆盖层进程暂时不可用，游戏和本地遥测仍在运行。"));
                    if (overlayFailureCount >= 3)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                        overlayFailureCount = 0;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task StopSessionAsync(Guid gameId, CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GameRuntimeSnapshot? runtime;
            lock (_syncRoot)
            {
                runtime = _runtime;
            }

            if (runtime?.GameId != gameId)
            {
                return;
            }

            await StopSessionCoreAsync(cancellationToken).ConfigureAwait(false);
            SetStatus(new OverlayRuntimeStatus(OverlayRuntimeState.Stopped, null, "游戏已退出，覆盖层会话已清理。"));
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task StopSessionCoreAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? trackingLifetime;
        Task? trackingTask;
        lock (_syncRoot)
        {
            trackingLifetime = _trackingLifetime;
            trackingTask = _trackingTask;
            _trackingLifetime = null;
            _trackingTask = null;
            _runtime = null;
            _profile = null;
        }

        trackingLifetime?.Cancel();
        await _overlayController.HideAsync(CancellationToken.None).ConfigureAwait(false);
        await _performanceTelemetry.StopAsync(CancellationToken.None).ConfigureAwait(false);
        if (trackingTask is not null && trackingTask.Id != Task.CurrentId)
        {
            try
            {
                await trackingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (trackingLifetime?.IsCancellationRequested == true)
            {
            }
        }

        trackingLifetime?.Dispose();
    }

    private static TelemetryTarget CreateTelemetryTarget(GameRuntimeSnapshot runtime)
    {
        var processIds = runtime.Processes
            .Where(static process => process.Confidence == GameProcessConfidence.Confirmed)
            .Select(static process => process.ProcessId)
            .Distinct()
            .ToArray();
        return new TelemetryTarget(
            runtime.GameId,
            runtime.PrimaryProcessId ?? throw new InvalidOperationException("运行状态没有主进程。"),
            processIds);
    }

    private static bool SameConfirmedProcesses(GameRuntimeSnapshot left, GameRuntimeSnapshot right) =>
        left.Processes
            .Where(static process => process.Confidence == GameProcessConfidence.Confirmed)
            .Select(static process => process.ProcessId)
            .Order()
            .SequenceEqual(
                right.Processes
                    .Where(static process => process.Confidence == GameProcessConfidence.Confirmed)
                    .Select(static process => process.ProcessId)
                    .Order());

    private void SetStatus(OverlayRuntimeStatus status)
    {
        lock (_syncRoot)
        {
            _status = status;
        }

        StatusChanged?.Invoke(this, new OverlayRuntimeStatusEventArgs(status));
    }
}
