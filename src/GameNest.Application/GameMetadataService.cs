using GameNest.Domain;

namespace GameNest.Application;

public sealed class GameMetadataService(
    IGameLibraryRepository gameRepository,
    IGameMetadataRepository metadataRepository,
    IEnumerable<IMetadataProvider> providers)
{
    private readonly IReadOnlyList<IMetadataProvider> _providers = providers.ToArray();

    public IReadOnlyList<(string Id, string DisplayName)> Providers =>
        _providers.Select(static provider => (provider.Id, provider.DisplayName)).ToArray();

    public async Task<MetadataSearchResult> SearchAsync(
        MetadataSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);

        var candidates = new List<MetadataCandidate>();
        var failures = new List<MetadataProviderFailure>();
        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var providerCandidates = await provider
                    .SearchAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                candidates.AddRange(providerCandidates.Where(candidate =>
                    string.Equals(candidate.ProviderId, provider.Id, StringComparison.Ordinal)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(new MetadataProviderFailure(
                    provider.Id,
                    provider.DisplayName,
                    exception.Message));
            }
        }

        return new MetadataSearchResult(candidates, failures);
    }

    public async Task<Game> ApplyAsync(
        Guid gameId,
        MetadataCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var original = await GetRequiredGameAsync(gameId, cancellationToken).ConfigureAwait(false);
        var attribution = new GameMetadataAttribution(
            candidate.ProviderId,
            candidate.SourceId,
            candidate.ProviderName,
            DateTimeOffset.UtcNow);
        var updated = original.WithMetadata(candidate.Title, candidate.Description, attribution);

        await metadataRepository
            .ApplyAsync(original, updated, candidate, cancellationToken)
            .ConfigureAwait(false);
        return updated;
    }

    public async Task<Game?> UndoLastAsync(Guid gameId, CancellationToken cancellationToken)
    {
        if (gameId == Guid.Empty)
        {
            throw new ArgumentException("游戏 ID 不能为空。", nameof(gameId));
        }

        return await metadataRepository.UndoLastAsync(gameId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Game> GetRequiredGameAsync(Guid gameId, CancellationToken cancellationToken)
    {
        if (gameId == Guid.Empty)
        {
            throw new ArgumentException("游戏 ID 不能为空。", nameof(gameId));
        }

        return await gameRepository.GetByIdAsync(gameId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("找不到指定游戏。");
    }
}
