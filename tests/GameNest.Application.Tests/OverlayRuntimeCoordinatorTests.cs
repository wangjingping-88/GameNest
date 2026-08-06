using GameNest.Domain;

namespace GameNest.Application.Tests;

public sealed class OverlayRuntimeCoordinatorTests
{
    [Fact]
    public async Task RunningGameStartsTelemetryAndOverlayThenCleansUpOnExit()
    {
        var launcher = new RuntimeLaunchService();
        var library = new GameLibraryService(
            new UnusedGameRepository(),
            new UnusedFileInspector(),
            new UnusedAssetService(),
            launcher,
            new MemoryGameRuntimeRepository());
        var overlay = new RecordingOverlayController();
        var telemetry = new RecordingTelemetry();
        var settings = new OverlaySettingsService(
            new MemoryOverlayProfileRepository(),
            overlay,
            telemetry);
        await using var coordinator = new OverlayRuntimeCoordinator(
            library,
            settings,
            telemetry,
            new FixedWindowLocator(),
            overlay);
        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        var gameId = Guid.NewGuid();
        var running = CreateRuntime(gameId, GameRuntimeState.Running);

        launcher.Raise(running);
        await overlay.FrameReceived.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Equal(gameId, telemetry.StartedTarget?.GameId);
        Assert.Equal(42, telemetry.StartedTarget?.PrimaryProcessId);
        Assert.Equal(OverlayRuntimeState.Active, coordinator.Status.State);

        launcher.Raise(CreateRuntime(gameId, GameRuntimeState.NotRunning));
        await telemetry.Stopped.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);
        Assert.True(overlay.HideCount > 0);
    }

    private static GameRuntimeSnapshot CreateRuntime(Guid gameId, GameRuntimeState state) =>
        new(
            gameId,
            state,
            state == GameRuntimeState.NotRunning ? null : 42,
            state == GameRuntimeState.NotRunning
                ? GameProcessConfidence.Unconfirmed
                : GameProcessConfidence.Confirmed,
            state == GameRuntimeState.NotRunning ? null : DateTimeOffset.UtcNow,
            state == GameRuntimeState.NotRunning
                ? []
                :
                [
                    new TrackedGameProcess(
                        42,
                        null,
                        "Game",
                        @"D:\Games\Game.exe",
                        DateTimeOffset.UtcNow,
                        GameProcessConfidence.Confirmed),
                ]);

    private sealed class RuntimeLaunchService : IGameLaunchService
    {
        private GameRuntimeSnapshot? _runtime;

        public event EventHandler<GameProcessStatusChangedEventArgs>? StatusChanged;

        public bool IsRunning(Guid gameId) => _runtime?.GameId == gameId && _runtime.IsRunning;

        public GameRuntimeSnapshot? GetRuntime(Guid gameId) =>
            _runtime?.GameId == gameId ? _runtime : null;

        public Task<GameLaunchResult> LaunchAsync(Game game, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GameStopResult> StopAsync(Guid gameId, bool force, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Raise(GameRuntimeSnapshot runtime)
        {
            _runtime = runtime;
            StatusChanged?.Invoke(this, new GameProcessStatusChangedEventArgs(runtime));
        }
    }

    private sealed class RecordingTelemetry : IPerformanceTelemetry
    {
        public event EventHandler<PerformanceSnapshotEventArgs>? SnapshotAvailable;

        public PerformanceSnapshot? Current { get; private set; }

        public TelemetryTarget? StartedTarget { get; private set; }

        public TaskCompletionSource Stopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TelemetryCapabilityReport> CheckCapabilityAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StartAsync(TelemetryTarget target, CancellationToken cancellationToken)
        {
            StartedTarget = target;
            Current = new PerformanceSnapshot(
                target.GameId,
                DateTimeOffset.UtcNow,
                TelemetryMetric.Available(60),
                TelemetryMetric.Available(10),
                TelemetryMetric.Available(20),
                TelemetryMetric.Available(512 * 1024 * 1024));
            SnapshotAvailable?.Invoke(this, new PerformanceSnapshotEventArgs(Current));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (StartedTarget is not null)
            {
                Current = null;
                StartedTarget = null;
                Stopped.TrySetResult();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOverlayController : IOverlayController
    {
        public event EventHandler<OverlayControllerStatusEventArgs>? StatusChanged;

        public OverlayControllerStatus Status { get; private set; } =
            new(OverlayControllerState.Stopped, true, "测试");

        public TaskCompletionSource<OverlayFrame> FrameReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int HideCount { get; private set; }

        public Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            Status = new OverlayControllerStatus(OverlayControllerState.Ready, true, "测试");
            StatusChanged?.Invoke(this, new OverlayControllerStatusEventArgs(Status));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(OverlayFrame frame, CancellationToken cancellationToken)
        {
            FrameReceived.TrySetResult(frame);
            return Task.CompletedTask;
        }

        public Task HideAsync(CancellationToken cancellationToken)
        {
            HideCount++;
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> IsHotkeyAvailableAsync(OverlayHotkey hotkey, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class FixedWindowLocator : IGameWindowLocator
    {
        public Task<GameWindowSnapshot?> FindPrimaryWindowAsync(
            GameRuntimeSnapshot runtime,
            CancellationToken cancellationToken) =>
            Task.FromResult<GameWindowSnapshot?>(
                new GameWindowSnapshot(
                    100,
                    new GameWindowBounds(0, 0, 1280, 720),
                    96,
                    true,
                    false,
                    false));
    }

    private sealed class MemoryOverlayProfileRepository : IOverlayProfileRepository
    {
        private readonly OverlayProfile _global = OverlayProfile.CreateDefault();

        public Task<OverlayProfile> GetGlobalAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_global);

        public Task<OverlayProfile?> GetForGameAsync(Guid gameId, CancellationToken cancellationToken) =>
            Task.FromResult<OverlayProfile?>(null);

        public Task SaveAsync(OverlayProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RemoveForGameAsync(Guid gameId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UnusedGameRepository : IGameLibraryRepository
    {
        public Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Game>>([]);

        public Task<Game?> GetByIdAsync(Guid gameId, CancellationToken cancellationToken) =>
            Task.FromResult<Game?>(null);

        public Task<Game?> FindByExecutablePathAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult<Game?>(null);

        public Task AddAsync(Game game, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateAsync(Game game, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetIconAsync(GameAsset icon, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetCoverAsync(
            GameAsset cover,
            bool isUserEdited,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveCoverAsync(Guid gameId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetAvailabilityByVolumeAsync(
            string volumeIdentity,
            GameAvailability availability,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RebindVolumeAsync(
            string volumeIdentity,
            string previousRoot,
            string currentRoot,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RemoveAsync(Guid gameId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedFileInspector : ILocalGameFileInspector
    {
        public Task<LocalGameFileInspection> InspectAsync(string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedAssetService : IGameAssetService
    {
        public Task<GameAsset?> ExtractIconAsync(
            Guid gameId,
            LocalGameFileInspection inspection,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GameAsset?> DiscoverCoverAsync(
            Guid gameId,
            string installRoot,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GameAsset> ImportCoverAsync(
            Guid gameId,
            string sourcePath,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
