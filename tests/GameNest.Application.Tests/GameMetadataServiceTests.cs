using GameNest.Application;
using GameNest.Domain;

namespace GameNest.Application.Tests;

public sealed class GameMetadataServiceTests
{
    [Fact]
    public async Task SearchKeepsSuccessfulResultsWhenAnotherProviderIsOffline()
    {
        var repository = new MemoryGameRepository(CreateGame([]));
        var service = new GameMetadataService(
            repository,
            new MemoryMetadataRepository(repository),
            [new SuccessfulProvider(), new OfflineProvider()]);

        var result = await service.SearchAsync(
            new MetadataSearchRequest("Local Game"),
            TestContext.Current.CancellationToken);

        Assert.Single(result.Candidates);
        Assert.Single(result.Failures);
        Assert.Equal("offline", result.Failures[0].ProviderId);
        Assert.NotNull(await repository.GetByIdAsync(repository.Game.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApplyPreservesManualTitleAndUndoRestoresPreviousMetadata()
    {
        var original = CreateGame([GameEditableField.Title]);
        var repository = new MemoryGameRepository(original);
        var metadataRepository = new MemoryMetadataRepository(repository);
        var service = new GameMetadataService(repository, metadataRepository, [new SuccessfulProvider()]);
        var candidate = new MetadataCandidate(
            "success",
            "测试提供者",
            "source-42",
            "错误的在线标题",
            "在线补全简介");

        var applied = await service.ApplyAsync(
            original.Id,
            candidate,
            TestContext.Current.CancellationToken);
        var undone = await service.UndoLastAsync(
            original.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal("本地标题", applied.Title);
        Assert.Equal("在线补全简介", applied.Description);
        Assert.Equal("success", applied.MetadataAttribution?.ProviderId);
        Assert.Equal(original.Title, undone?.Title);
        Assert.Equal(original.Description, undone?.Description);
        Assert.Null(undone?.MetadataAttribution);
    }

    private static Game CreateGame(IEnumerable<GameEditableField> editedFields)
    {
        var gameId = Guid.NewGuid();
        return new Game(
            gameId,
            "本地标题",
            null,
            @"D:\Games\Local",
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
                @"D:\Games\Local\game.exe",
                null,
                @"D:\Games\Local",
                false,
                true),
            null,
            userEditedFields: editedFields);
    }

    private sealed class SuccessfulProvider : IMetadataProvider
    {
        public string Id => "success";

        public string DisplayName => "测试提供者";

        public Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
            MetadataSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetadataCandidate>>(
            [
                new MetadataCandidate(Id, DisplayName, "source-42", request.Query, "在线简介"),
            ]);
    }

    private sealed class OfflineProvider : IMetadataProvider
    {
        public string Id => "offline";

        public string DisplayName => "离线提供者";

        public Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
            MetadataSearchRequest request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("网络不可用");
    }

    private sealed class MemoryMetadataRepository(MemoryGameRepository games) : IGameMetadataRepository
    {
        private readonly Stack<Game> _revisions = new();

        public Task ApplyAsync(
            Game original,
            Game updated,
            MetadataCandidate candidate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _revisions.Push(original);
            games.Game = updated;
            return Task.CompletedTask;
        }

        public Task<Game?> UndoLastAsync(Guid gameId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_revisions.Count == 0)
            {
                return Task.FromResult<Game?>(null);
            }

            games.Game = _revisions.Pop();
            return Task.FromResult<Game?>(games.Game);
        }
    }

    private sealed class MemoryGameRepository(Game game) : IGameLibraryRepository
    {
        public Game Game { get; set; } = game;

        public Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Game>>([Game]);

        public Task<Game?> GetByIdAsync(Guid gameId, CancellationToken cancellationToken) =>
            Task.FromResult<Game?>(gameId == Game.Id ? Game : null);

        public Task<Game?> FindByExecutablePathAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult<Game?>(null);

        public Task AddAsync(Game gameToAdd, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateAsync(Game updated, CancellationToken cancellationToken)
        {
            Game = updated;
            return Task.CompletedTask;
        }

        public Task SetIconAsync(GameAsset icon, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetCoverAsync(GameAsset cover, bool isUserEdited, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveCoverAsync(Guid gameId, CancellationToken cancellationToken) => throw new NotSupportedException();

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
}
