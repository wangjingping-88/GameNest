using GameNest.Application;
using GameNest.Domain;

namespace GameNest.Application.Tests;

public sealed class GameScanServiceTests
{
    [Fact]
    public async Task RunAsyncWaitsWhilePausedThenPersistsGroupedCandidates()
    {
        var scanRepository = new MemoryScanRepository(CreateRoot());
        var adapter = new PauseAwareAdapter();
        var service = CreateService(scanRepository, adapter);
        var pause = new ScanPauseController();
        pause.Pause();

        var run = service.RunAsync(
            ScanMode.Quick,
            pause,
            null,
            TestContext.Current.CancellationToken);
        await adapter.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        Assert.False(run.IsCompleted);

        pause.Resume();
        var summary = await run;

        Assert.False(summary.WasCancelled);
        Assert.Equal(GameScanRunStatus.Completed, scanRepository.CompletedStatus);
        Assert.Equal(GameCandidateConfidence.High, Assert.Single(scanRepository.Candidates).Confidence);
    }

    [Fact]
    public async Task RunAsyncCancellationStopsPausedAdapterAndMarksRunCancelled()
    {
        var scanRepository = new MemoryScanRepository(CreateRoot());
        var adapter = new PauseAwareAdapter();
        var service = CreateService(scanRepository, adapter);
        var pause = new ScanPauseController();
        pause.Pause();
        using var cancellation = new CancellationTokenSource();

        var run = service.RunAsync(ScanMode.Deep, pause, null, cancellation.Token);
        await adapter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var summary = await run;

        Assert.True(summary.WasCancelled);
        Assert.Equal(GameScanRunStatus.Cancelled, scanRepository.CompletedStatus);
    }

    private static GameScanService CreateService(
        MemoryScanRepository scanRepository,
        IGameSourceAdapter adapter)
    {
        var gameRepository = new MemoryGameRepository();
        var library = new GameLibraryService(
            gameRepository,
            new StubFileInspector(),
            new StubAssetService(),
            new StubLaunchService(),
            new MemoryGameRuntimeRepository());
        return new GameScanService(
            [adapter],
            new GameCandidateScorer(),
            new GameCandidateGrouper(),
            scanRepository,
            new StubVolumeIdentityService(),
            gameRepository,
            library);
    }

    private static ScanRoot CreateRoot() =>
        new(
            Guid.NewGuid(),
            "volume-1",
            @"D:\Games",
            "Games",
            ScanMode.Quick,
            true,
            true,
            null,
            null);

    private sealed class PauseAwareAdapter : IGameSourceAdapter
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => "pause-aware";

        public async Task<IReadOnlyList<DiscoveredGame>> ScanAsync(
            GameScanContext context,
            IProgress<GameScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            _ = progress;
            Entered.TrySetResult();
            await context.PauseToken.WaitWhilePausedAsync(cancellationToken);
            return
            [
                new DiscoveredGame(
                    context.Roots[0].Id,
                    Id,
                    GameCandidateSource.GenericExecutable,
                    null,
                    "Example",
                    @"D:\Games\Example\Example.exe",
                    null,
                    @"D:\Games\Example",
                    @"D:\Games\Example",
                    "volume-1",
                    1024 * 1024,
                    DateTimeOffset.UtcNow,
                    [
                        new GameCandidateEvidence("steam-api", "Steam API", 35),
                        new GameCandidateEvidence("engine-layout", "引擎目录", 25),
                    ])
            ];
        }
    }

    private sealed class MemoryScanRepository(ScanRoot root) : IGameScanRepository
    {
        public IReadOnlyList<GameCandidate> Candidates { get; private set; } = [];

        public GameScanRunStatus? CompletedStatus { get; private set; }

        public Task<IReadOnlyList<ScanRoot>> GetRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScanRoot>>([root]);

        public Task AddRootAsync(ScanRoot value, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpdateRootAsync(ScanRoot value, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> RemoveRootAsync(Guid rootId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyList<GameCandidate>> GetCandidatesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Candidates);

        public Task<GameCandidate?> GetCandidateAsync(Guid candidateId, CancellationToken cancellationToken) =>
            Task.FromResult(Candidates.FirstOrDefault(candidate => candidate.Id == candidateId));

        public Task<Guid> StartRunAsync(ScanMode mode, CancellationToken cancellationToken) =>
            Task.FromResult(Guid.NewGuid());

        public Task SaveCandidatesAsync(
            Guid runId,
            IReadOnlyList<GameCandidate> candidates,
            CancellationToken cancellationToken)
        {
            Candidates = candidates;
            return Task.CompletedTask;
        }

        public Task CompleteRunAsync(
            Guid runId,
            GameScanRunStatus status,
            long checkedDirectoryCount,
            int candidateCount,
            string? errorMessage,
            CancellationToken cancellationToken)
        {
            CompletedStatus = status;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetExcludedDirectoriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task AddExcludedDirectoryAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> UndoLastExcludedDirectoryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task SetCandidateDecisionAsync(
            Guid candidateId,
            GameCandidateDecision decision,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubVolumeIdentityService : IVolumeIdentityService
    {
        public Task<VolumeLocation> ResolveAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(new VolumeLocation("volume-1", @"D:\", path, "Games", true));

        public Task<VolumeLocation?> FindAsync(
            string volumeIdentity,
            string relativePath,
            CancellationToken cancellationToken) =>
            Task.FromResult<VolumeLocation?>(
                new VolumeLocation(volumeIdentity, @"D:\", @"D:\Games", relativePath, true));
    }

    private sealed class MemoryGameRepository : IGameLibraryRepository
    {
        public Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Game>>([]);

        public Task<Game?> GetByIdAsync(Guid gameId, CancellationToken cancellationToken) =>
            Task.FromResult<Game?>(null);

        public Task<Game?> FindByExecutablePathAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult<Game?>(null);

        public Task AddAsync(Game game, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpdateAsync(Game game, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetIconAsync(GameAsset icon, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetCoverAsync(
            GameAsset cover,
            bool isUserEdited,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RemoveCoverAsync(Guid gameId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetAvailabilityByVolumeAsync(
            string volumeIdentity,
            GameAvailability availability,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RebindVolumeAsync(
            string volumeIdentity,
            string previousRoot,
            string currentRoot,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> RemoveAsync(Guid gameId, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class StubFileInspector : ILocalGameFileInspector
    {
        public Task<LocalGameFileInspection> InspectAsync(string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class StubAssetService : IGameAssetService
    {
        public Task<GameAsset?> ExtractIconAsync(
            Guid gameId,
            LocalGameFileInspection inspection,
            CancellationToken cancellationToken) => Task.FromResult<GameAsset?>(null);

        public Task<GameAsset?> DiscoverCoverAsync(
            Guid gameId,
            string installRoot,
            CancellationToken cancellationToken) => Task.FromResult<GameAsset?>(null);

        public Task<GameAsset> ImportCoverAsync(
            Guid gameId,
            string sourcePath,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubLaunchService : IGameLaunchService
    {
        public event EventHandler<GameProcessStatusChangedEventArgs>? StatusChanged
        {
            add { }
            remove { }
        }

        public bool IsRunning(Guid gameId) => false;

        public GameRuntimeSnapshot? GetRuntime(Guid gameId) => null;

        public Task<GameLaunchResult> LaunchAsync(Game game, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GameStopResult> StopAsync(
            Guid gameId,
            bool force,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
