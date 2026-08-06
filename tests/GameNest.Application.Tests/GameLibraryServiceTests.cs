using GameNest.Application;
using GameNest.Domain;

namespace GameNest.Application.Tests;

public sealed class GameLibraryServiceTests
{
    [Fact]
    public async Task AddEditFavoriteSearchRemoveRoundTripUsesApplicationService()
    {
        var repository = new MemoryGameLibraryRepository();
        var service = CreateService(repository);

        var added = await service.AddAsync(
            @"D:\游戏 库\RPG [特别版]\冒险.exe",
            new GameEditorInput(Description: "初始简介"),
            TestContext.Current.CancellationToken);
        var edited = await service.UpdateAsync(
            added.Id,
            new GameEditorInput("星海冒险", "用户编辑后的简介", "--windowed", @"D:\游戏 库\RPG [特别版]"),
            TestContext.Current.CancellationToken);
        var favorite = await service.SetFavoriteAsync(
            edited.Id,
            true,
            TestContext.Current.CancellationToken);

        var searchResult = await service.GetGamesAsync(
            new GameLibraryQuery("星海", FavoritesOnly: true),
            TestContext.Current.CancellationToken);

        Assert.Single(searchResult);
        Assert.Equal("用户编辑后的简介", searchResult[0].Description);
        Assert.Equal("--windowed", searchResult[0].LaunchProfile.Arguments);
        Assert.True(favorite.IsFavorite);
        Assert.True(await service.RemoveAsync(added.Id, TestContext.Current.CancellationToken));
        Assert.Empty(await service.GetGamesAsync(new(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddAsyncRejectsDuplicateResolvedExecutable()
    {
        var repository = new MemoryGameLibraryRepository();
        var service = CreateService(repository);
        const string path = @"D:\Games\Duplicate.exe";

        await service.AddAsync(path, new(), TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddAsync(path, new(), TestContext.Current.CancellationToken));
        Assert.Contains("已经在游戏库", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchAsyncUsesConfirmedProfileAndUpdatesLastPlayed()
    {
        var repository = new MemoryGameLibraryRepository();
        var launcher = new RecordingGameLaunchService();
        var service = CreateService(repository, launcher);
        var added = await service.AddAsync(
            @"D:\Games\启动 测试.exe",
            new(),
            TestContext.Current.CancellationToken);

        var result = await service.LaunchAsync(added.Id, TestContext.Current.CancellationToken);
        var stored = await repository.GetByIdAsync(added.Id, TestContext.Current.CancellationToken);

        Assert.Equal(4242, result.ProcessId);
        Assert.Equal(added.Id, launcher.LaunchedGameId);
        Assert.NotNull(stored?.LastPlayedUtc);
    }

    [Fact]
    public async Task AddAsyncSupportsTwentyDistinctUnicodeAndSpecialCharacterPaths()
    {
        var repository = new MemoryGameLibraryRepository();
        var service = CreateService(repository);

        for (var index = 0; index < 20; index++)
        {
            await service.AddAsync(
                $@"D:\游戏 库\第 {index:00} 款 [测试]\Game #{index:00}.exe",
                new(),
                TestContext.Current.CancellationToken);
        }

        var games = await service.GetGamesAsync(new(), TestContext.Current.CancellationToken);
        Assert.Equal(20, games.Count);
        Assert.Equal(20, games.Select(static game => game.LaunchProfile.ExecutablePath).Distinct().Count());
    }

    [Fact]
    public async Task AddAsyncReturnsBeforeIconExtractionAndRefreshPersistsIconLater()
    {
        var repository = new MemoryGameLibraryRepository();
        var assetService = new RecordingGameAssetService();
        var service = new GameLibraryService(
            repository,
            new StubLocalGameFileInspector(),
            assetService,
            new RecordingGameLaunchService(),
            new MemoryGameRuntimeRepository());

        var added = await service.AddAsync(
            @"D:\Games\快速添加.exe",
            new(),
            TestContext.Current.CancellationToken);

        Assert.False(assetService.WasCalled);
        Assert.Null(added.Icon);

        var refreshed = await service.RefreshIconAsync(
            added.Id,
            TestContext.Current.CancellationToken);

        Assert.True(assetService.WasCalled);
        Assert.NotNull(refreshed.Icon);
        Assert.NotNull((await repository.GetByIdAsync(
            added.Id,
            TestContext.Current.CancellationToken))?.Icon);
    }

    [Fact]
    public async Task RecoverInterruptedSessionsCompletesStaleActiveSession()
    {
        var repository = new MemoryGameLibraryRepository();
        var runtimeRepository = new MemoryGameRuntimeRepository();
        var service = new GameLibraryService(
            repository,
            new StubLocalGameFileInspector(),
            new NullGameAssetService(),
            new RecordingGameLaunchService(),
            runtimeRepository);
        var game = await service.AddAsync(
            @"D:\Games\中断恢复.exe",
            new(),
            TestContext.Current.CancellationToken);
        var session = new PlaySession(
            Guid.NewGuid(),
            game.Id,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            null,
            null,
            null,
            [7123]);
        await runtimeRepository.StartSessionAsync(session, TestContext.Current.CancellationToken);

        var recovered = await service.RecoverInterruptedSessionsAsync(
            TestContext.Current.CancellationToken);
        var stored = Assert.Single(
            await runtimeRepository.GetSessionsAsync(game.Id, TestContext.Current.CancellationToken));

        Assert.Equal(1, recovered);
        Assert.Equal(GameExitKind.TrackingLost, stored.ExitKind);
    }

    private static GameLibraryService CreateService(
        MemoryGameLibraryRepository repository,
        RecordingGameLaunchService? launcher = null) =>
        new(
            repository,
            new StubLocalGameFileInspector(),
            new NullGameAssetService(),
            launcher ?? new(),
            new MemoryGameRuntimeRepository());

    private sealed class MemoryGameLibraryRepository : IGameLibraryRepository
    {
        private readonly Dictionary<Guid, Game> _games = [];

        public Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Game>>(_games.Values.ToArray());
        }

        public Task<Game?> GetByIdAsync(Guid gameId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_games.GetValueOrDefault(gameId));
        }

        public Task<Game?> FindByExecutablePathAsync(
            string executablePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                _games.Values.FirstOrDefault(
                    game => string.Equals(
                        game.LaunchProfile.ExecutablePath,
                        executablePath,
                        StringComparison.OrdinalIgnoreCase)));
        }

        public Task AddAsync(Game game, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _games.Add(game.Id, game);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Game game, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _games[game.Id] = game;
            return Task.CompletedTask;
        }

        public Task SetIconAsync(GameAsset icon, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _games[icon.GameId] = _games[icon.GameId].WithIcon(icon);
            return Task.CompletedTask;
        }

        public Task SetCoverAsync(
            GameAsset cover,
            bool isUserEdited,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _games[cover.GameId] = _games[cover.GameId].WithCover(cover, isUserEdited);
            return Task.CompletedTask;
        }

        public Task RemoveCoverAsync(Guid gameId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _games[gameId] = _games[gameId].WithCover(null, isUserEdited: true);
            return Task.CompletedTask;
        }

        public Task SetAvailabilityByVolumeAsync(
            string volumeIdentity,
            GameAvailability availability,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var game in _games.Values
                         .Where(game => game.DiscoveryMetadata?.VolumeIdentity == volumeIdentity)
                         .ToArray())
            {
                _games[game.Id] = game.WithAvailability(availability);
            }

            return Task.CompletedTask;
        }

        public Task RebindVolumeAsync(
            string volumeIdentity,
            string previousRoot,
            string currentRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(Guid gameId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_games.Remove(gameId));
        }
    }

    private sealed class StubLocalGameFileInspector : ILocalGameFileInspector
    {
        public Task<LocalGameFileInspection> InspectAsync(
            string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var title = Path.GetFileNameWithoutExtension(path);
            var directory = Path.GetDirectoryName(path) ?? @"D:\";
            return Task.FromResult(
                new LocalGameFileInspection(
                    path,
                    path,
                    title,
                    null,
                    directory,
                    GameSourceType.ManualExecutable,
                    LaunchKind.Executable,
                    path));
        }

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }
    }

    private sealed class NullGameAssetService : IGameAssetService
    {
        public Task<GameAsset?> ExtractIconAsync(
            Guid gameId,
            LocalGameFileInspection inspection,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<GameAsset?>(null);
        }

        public Task<GameAsset?> DiscoverCoverAsync(
            Guid gameId,
            string installRoot,
            CancellationToken cancellationToken) =>
            Task.FromResult<GameAsset?>(null);

        public Task<GameAsset> ImportCoverAsync(
            Guid gameId,
            string sourcePath,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingGameAssetService : IGameAssetService
    {
        public bool WasCalled { get; private set; }

        public Task<GameAsset?> ExtractIconAsync(
            Guid gameId,
            LocalGameFileInspection inspection,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WasCalled = true;
            return Task.FromResult<GameAsset?>(
                new GameAsset(
                    Guid.NewGuid(),
                    gameId,
                    GameAssetType.Icon,
                    @"D:\GameNest\assets\icon.bmp",
                    inspection.IconSourcePath,
                    64,
                    64,
                    DateTimeOffset.UtcNow));
        }

        public Task<GameAsset?> DiscoverCoverAsync(
            Guid gameId,
            string installRoot,
            CancellationToken cancellationToken) =>
            Task.FromResult<GameAsset?>(null);

        public Task<GameAsset> ImportCoverAsync(
            Guid gameId,
            string sourcePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new GameAsset(
                    Guid.NewGuid(),
                    gameId,
                    GameAssetType.Cover,
                    sourcePath,
                    "UserImport",
                    600,
                    900,
                    DateTimeOffset.UtcNow));
    }

    private sealed class RecordingGameLaunchService : IGameLaunchService
    {
        public event EventHandler<GameProcessStatusChangedEventArgs>? StatusChanged;

        public Guid? LaunchedGameId { get; private set; }

        public bool IsRunning(Guid gameId) => LaunchedGameId == gameId;

        public GameRuntimeSnapshot? GetRuntime(Guid gameId) =>
            LaunchedGameId == gameId
                ? CreateRuntime(gameId)
                : null;

        public Task<GameLaunchResult> LaunchAsync(Game game, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LaunchedGameId = game.Id;
            var runtime = CreateRuntime(game.Id);
            StatusChanged?.Invoke(
                this,
                new GameProcessStatusChangedEventArgs(runtime));
            return Task.FromResult(
                new GameLaunchResult(
                    game.Id,
                    4242,
                    GameRuntimeState.Running,
                    GameProcessConfidence.Confirmed));
        }

        public Task<GameStopResult> StopAsync(
            Guid gameId,
            bool force,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new GameStopResult(gameId, GameStopOutcome.Stopped, [], "已停止"));

        private static GameRuntimeSnapshot CreateRuntime(Guid gameId) =>
            new(
                gameId,
                GameRuntimeState.Running,
                4242,
                GameProcessConfidence.Confirmed,
                DateTimeOffset.UtcNow,
                [new TrackedGameProcess(4242, null, "game", null, null, GameProcessConfidence.Confirmed)]);
    }
}
