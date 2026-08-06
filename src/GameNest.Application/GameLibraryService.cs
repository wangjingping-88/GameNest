using GameNest.Domain;
using System.Collections.Concurrent;

namespace GameNest.Application;

public sealed class GameLibraryService(
    IGameLibraryRepository repository,
    ILocalGameFileInspector fileInspector,
    IGameAssetService assetService,
    IGameCoverSearchProvider coverSearchProvider,
    IGameLaunchService launchService,
    IGameRuntimeRepository runtimeRepository)
{
    private static readonly IGameCoverSearchProvider NoOnlineCovers = new EmptyCoverSearchProvider();
    private readonly ConcurrentDictionary<Guid, LocalGameFileInspection> _pendingIconInspections = new();

    public GameLibraryService(
        IGameLibraryRepository repository,
        ILocalGameFileInspector fileInspector,
        IGameAssetService assetService,
        IGameLaunchService launchService,
        IGameRuntimeRepository runtimeRepository)
        : this(repository, fileInspector, assetService, NoOnlineCovers, launchService, runtimeRepository)
    {
    }

    public event EventHandler<GameProcessStatusChangedEventArgs>? RuntimeStatusChanged
    {
        add => launchService.StatusChanged += value;
        remove => launchService.StatusChanged -= value;
    }

    public async Task<IReadOnlyList<Game>> GetGamesAsync(
        GameLibraryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var games = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        IEnumerable<Game> filtered = games;

        if (query.FavoritesOnly)
        {
            filtered = filtered.Where(static game => game.IsFavorite);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var searchText = query.SearchText.Trim();
            filtered = filtered.Where(
                game => game.Title.Contains(searchText, StringComparison.CurrentCultureIgnoreCase));
        }

        return filtered
            .OrderBy(static game => game.SortTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<Game> AddAsync(
        string sourcePath,
        GameEditorInput input,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(input);

        var inspection = await fileInspector.InspectAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var existing = await repository
            .FindByExecutablePathAsync(inspection.ExecutablePath, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            throw new InvalidOperationException($"“{existing.Title}”已经在游戏库中。");
        }

        var gameId = Guid.NewGuid();
        var title = NormalizeTitle(input.Title, inspection.SuggestedTitle);
        var workingDirectory = NormalizeWorkingDirectory(input.WorkingDirectory, inspection.WorkingDirectory);
        var profile = new LaunchProfile(
            Guid.NewGuid(),
            gameId,
            "默认",
            inspection.LaunchKind,
            inspection.ExecutablePath,
            input.Arguments ?? inspection.Arguments,
            workingDirectory,
            runAsAdministrator: false,
            isDefault: true);
        var game = new Game(
            gameId,
            title,
            input.Description,
            inspection.WorkingDirectory,
            inspection.SourceType,
            isFavorite: false,
            GameAvailability.Available,
            DateTimeOffset.UtcNow,
            lastPlayedUtc: null,
            totalPlaySeconds: 0,
            profile,
            icon: null,
            userEditedFields: GetManualFields(input));

        await repository.AddAsync(game, cancellationToken).ConfigureAwait(false);
        _pendingIconInspections[gameId] = inspection;
        return game;
    }

    public async Task<Game> RefreshIconAsync(Guid gameId, CancellationToken cancellationToken)
    {
        var game = await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
        if (game.Icon is not null)
        {
            return game;
        }

        var inspection = _pendingIconInspections.TryRemove(gameId, out var pending)
            ? pending
            : new LocalGameFileInspection(
                game.LaunchProfile.ExecutablePath,
                game.LaunchProfile.ExecutablePath,
                game.Title,
                game.LaunchProfile.Arguments,
                game.LaunchProfile.WorkingDirectory,
                game.SourceType,
                game.LaunchProfile.LaunchKind,
                game.LaunchProfile.ExecutablePath);
        var icon = await assetService
            .ExtractIconAsync(gameId, inspection, cancellationToken)
            .ConfigureAwait(false);
        if (icon is null)
        {
            return game;
        }

        await repository.SetIconAsync(icon, cancellationToken).ConfigureAwait(false);
        return await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Game> RefreshAssetsAsync(Guid gameId, CancellationToken cancellationToken)
    {
        var game = await RefreshIconAsync(gameId, cancellationToken).ConfigureAwait(false);
        if (game.Cover is not null || game.UserEditedFields.Contains(GameEditableField.Cover))
        {
            return game;
        }

        var cover = await assetService
            .DiscoverCoverAsync(gameId, game.InstallRoot, cancellationToken)
            .ConfigureAwait(false);
        if (cover is not null)
        {
            await repository.SetCoverAsync(cover, isUserEdited: false, cancellationToken)
                .ConfigureAwait(false);
            return await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
        }

        var onlineCover = (await coverSearchProvider.SearchAsync(game.Title, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(static candidate => candidate.IsExactTitleMatch);
        if (onlineCover is null)
        {
            return game;
        }

        cover = await assetService
            .ImportCoverFromUriAsync(gameId, onlineCover.ImageUri, onlineCover.SourceName, cancellationToken)
            .ConfigureAwait(false);
        await repository.SetCoverAsync(cover, isUserEdited: false, cancellationToken).ConfigureAwait(false);
        return await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<GameCoverCandidate>> SearchOnlineCoversAsync(
        string title,
        CancellationToken cancellationToken) => coverSearchProvider.SearchAsync(title, cancellationToken);

    public async Task<Game> ApplyOnlineCoverAsync(
        Guid gameId,
        GameCoverCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        _ = await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
        var cover = await assetService
            .ImportCoverFromUriAsync(gameId, candidate.ImageUri, candidate.SourceName, cancellationToken)
            .ConfigureAwait(false);
        await repository.SetCoverAsync(cover, isUserEdited: false, cancellationToken).ConfigureAwait(false);
        return await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Game> ImportCoverAsync(
        Guid gameId,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        _ = await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
        var cover = await assetService.ImportCoverAsync(gameId, sourcePath, cancellationToken)
            .ConfigureAwait(false);
        await repository.SetCoverAsync(cover, isUserEdited: true, cancellationToken)
            .ConfigureAwait(false);
        return await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Game> RemoveCoverAsync(Guid gameId, CancellationToken cancellationToken)
    {
        _ = await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
        await repository.RemoveCoverAsync(gameId, cancellationToken).ConfigureAwait(false);
        return await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Game> ImportCandidateAsync(
        GameCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var existing = await repository
            .FindByExecutablePathAsync(candidate.ExecutablePath, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var gameId = Guid.NewGuid();
        var sourceType = candidate.Source switch
        {
            GameCandidateSource.Steam => GameSourceType.Steam,
            GameCandidateSource.Shortcut => GameSourceType.DiscoveredShortcut,
            GameCandidateSource.GenericExecutable => GameSourceType.DiscoveredExecutable,
            _ => throw new ArgumentOutOfRangeException(nameof(candidate)),
        };
        var game = new Game(
            gameId,
            candidate.Title,
            description: null,
            candidate.InstallRoot,
            sourceType,
            isFavorite: false,
            GameAvailability.Available,
            DateTimeOffset.UtcNow,
            lastPlayedUtc: null,
            totalPlaySeconds: 0,
            new LaunchProfile(
                Guid.NewGuid(),
                gameId,
                "默认",
                LaunchKind.Executable,
                candidate.ExecutablePath,
                candidate.Arguments,
                candidate.WorkingDirectory,
                runAsAdministrator: false,
                isDefault: true),
            icon: null,
            new GameDiscoveryMetadata(
                candidate.SourceGameId,
                candidate.VolumeIdentity,
                candidate.Score));
        await repository.AddAsync(game, cancellationToken).ConfigureAwait(false);
        return game;
    }

    public async Task<Game> UpdateAsync(
        Guid gameId,
        GameEditorInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var game = await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
        var updated = game.WithUserEdits(
            NormalizeTitle(input.Title, game.Title),
            input.Description,
            input.Arguments,
            NormalizeWorkingDirectory(input.WorkingDirectory, game.LaunchProfile.WorkingDirectory));
        await repository.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<Game> SetFavoriteAsync(
        Guid gameId,
        bool isFavorite,
        CancellationToken cancellationToken)
    {
        var game = await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
        var updated = game.WithFavorite(isFavorite);
        await repository.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public Task<bool> RemoveAsync(Guid gameId, CancellationToken cancellationToken) =>
        repository.RemoveAsync(gameId, cancellationToken);

    public async Task<GameLaunchResult> LaunchAsync(Guid gameId, CancellationToken cancellationToken)
    {
        var game = await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
        if (!await fileInspector
                .FileExistsAsync(game.LaunchProfile.ExecutablePath, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new FileNotFoundException("游戏主程序不存在，请编辑路径后重试。", game.LaunchProfile.ExecutablePath);
        }

        var result = await launchService.LaunchAsync(game, cancellationToken).ConfigureAwait(false);
        await repository
            .UpdateAsync(game.WithLastPlayed(DateTimeOffset.UtcNow), cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    public bool IsRunning(Guid gameId) => launchService.IsRunning(gameId);

    public GameRuntimeSnapshot? GetRuntime(Guid gameId) => launchService.GetRuntime(gameId);

    public Task<GameStopResult> StopAsync(
        Guid gameId,
        bool force,
        CancellationToken cancellationToken) =>
        launchService.StopAsync(gameId, force, cancellationToken);

    public Task<IReadOnlyList<PlaySession>> GetSessionsAsync(
        Guid gameId,
        CancellationToken cancellationToken) =>
        runtimeRepository.GetSessionsAsync(gameId, cancellationToken);

    public async Task<int> RecoverInterruptedSessionsAsync(CancellationToken cancellationToken)
    {
        var activeSessions = await runtimeRepository
            .GetActiveSessionsAsync(cancellationToken)
            .ConfigureAwait(false);
        var recoveredAtUtc = DateTimeOffset.UtcNow;
        var recoveredCount = 0;
        foreach (var session in activeSessions)
        {
            if (await runtimeRepository
                    .CompleteSessionAsync(
                        session.Id,
                        recoveredAtUtc,
                        GameExitKind.TrackingLost,
                        cancellationToken)
                    .ConfigureAwait(false) is not null)
            {
                recoveredCount++;
            }
        }

        return recoveredCount;
    }

    private async Task<Game> GetRequiredGameAsync(Guid gameId, CancellationToken cancellationToken)
    {
        if (gameId == Guid.Empty)
        {
            throw new ArgumentException("游戏 ID 不能为空。", nameof(gameId));
        }

        return await repository.GetByIdAsync(gameId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("找不到指定游戏。它可能已经从游戏库移除。");
    }

    private static string NormalizeTitle(string? title, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(title) ? fallback.Trim() : title.Trim();
        if (normalized.Length > 160)
        {
            throw new ArgumentException("游戏名称不能超过 160 个字符。", nameof(title));
        }

        return normalized;
    }

    private static string NormalizeWorkingDirectory(string? workingDirectory, string fallback) =>
        string.IsNullOrWhiteSpace(workingDirectory) ? fallback : workingDirectory.Trim();

    private static List<GameEditableField> GetManualFields(GameEditorInput input)
    {
        var fields = new List<GameEditableField>();
        if (!string.IsNullOrWhiteSpace(input.Title))
        {
            fields.Add(GameEditableField.Title);
        }

        if (!string.IsNullOrWhiteSpace(input.Description))
        {
            fields.Add(GameEditableField.Description);
        }

        if (!string.IsNullOrWhiteSpace(input.Arguments))
        {
            fields.Add(GameEditableField.Arguments);
        }

        if (!string.IsNullOrWhiteSpace(input.WorkingDirectory))
        {
            fields.Add(GameEditableField.WorkingDirectory);
        }

        return fields;
    }

    private sealed class EmptyCoverSearchProvider : IGameCoverSearchProvider
    {
        public Task<IReadOnlyList<GameCoverCandidate>> SearchAsync(
            string title,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GameCoverCandidate>>([]);
    }
}
