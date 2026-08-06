using System.Diagnostics;
using GameNest.Application;
using GameNest.Domain;

namespace GameNest.Application.Tests;

public sealed class GameLibraryThousandItemPerformanceTests
{
    [Fact]
    public async Task SearchAcrossOneThousandItemsStaysWithinInteractiveBudget()
    {
        var games = Enumerable.Range(0, 1000).Select(CreateGame).ToArray();
        var service = new GameLibraryService(
            new ReadOnlyGameRepository(games),
            new UnusedFileInspector(),
            new UnusedAssetService(),
            new UnusedLaunchService(),
            new MemoryGameRuntimeRepository());
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 50; index++)
        {
            var result = await service.GetGamesAsync(
                new GameLibraryQuery($"游戏 {index:0000}"),
                TestContext.Current.CancellationToken);
            Assert.Single(result);
        }

        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(1000),
            $"1000 条数据执行 50 次搜索耗时 {stopwatch.Elapsed.TotalMilliseconds:F0}ms，超过 1000ms 预算。");
    }

    private static Game CreateGame(int index)
    {
        var gameId = Guid.NewGuid();
        return new Game(
            gameId,
            $"游戏 {index:0000}",
            null,
            $@"D:\Games\Game{index:0000}",
            GameSourceType.ManualExecutable,
            false,
            GameAvailability.Available,
            DateTimeOffset.UtcNow,
            null,
            0,
            new LaunchProfile(
                Guid.NewGuid(),
                gameId,
                "默认",
                LaunchKind.Executable,
                $@"D:\Games\Game{index:0000}\game.exe",
                null,
                $@"D:\Games\Game{index:0000}",
                false,
                true),
            null);
    }

    private sealed class ReadOnlyGameRepository(IReadOnlyList<Game> games) : IGameLibraryRepository
    {
        public Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(games);
        public Task<Game?> GetByIdAsync(Guid gameId, CancellationToken cancellationToken) => Task.FromResult<Game?>(null);
        public Task<Game?> FindByExecutablePathAsync(string executablePath, CancellationToken cancellationToken) => Task.FromResult<Game?>(null);
        public Task AddAsync(Game game, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateAsync(Game game, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetIconAsync(GameAsset icon, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetCoverAsync(GameAsset cover, bool isUserEdited, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoveCoverAsync(Guid gameId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetAvailabilityByVolumeAsync(string volumeIdentity, GameAvailability availability, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RebindVolumeAsync(string volumeIdentity, string previousRoot, string currentRoot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Guid gameId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedFileInspector : ILocalGameFileInspector
    {
        public Task<LocalGameFileInspection> InspectAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedAssetService : IGameAssetService
    {
        public Task<GameAsset?> ExtractIconAsync(Guid gameId, LocalGameFileInspection inspection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GameAsset?> DiscoverCoverAsync(Guid gameId, string installRoot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GameAsset> ImportCoverAsync(Guid gameId, string sourcePath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedLaunchService : IGameLaunchService
    {
        public event EventHandler<GameProcessStatusChangedEventArgs>? StatusChanged { add { } remove { } }
        public bool IsRunning(Guid gameId) => false;
        public GameRuntimeSnapshot? GetRuntime(Guid gameId) => null;
        public Task<GameLaunchResult> LaunchAsync(Game game, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GameStopResult> StopAsync(Guid gameId, bool force, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
