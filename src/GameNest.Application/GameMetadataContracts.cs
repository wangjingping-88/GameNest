using GameNest.Domain;

namespace GameNest.Application;

public sealed record MetadataSearchRequest(
    string Query,
    string? ExecutableFileName = null,
    string? InstallDirectoryName = null);

public sealed record MetadataCandidate(
    string ProviderId,
    string ProviderName,
    string SourceId,
    string Title,
    string? Description = null);

public sealed record MetadataProviderFailure(
    string ProviderId,
    string ProviderName,
    string Message);

public sealed record MetadataSearchResult(
    IReadOnlyList<MetadataCandidate> Candidates,
    IReadOnlyList<MetadataProviderFailure> Failures);

public interface IMetadataProvider
{
    string Id { get; }

    string DisplayName { get; }

    Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
        MetadataSearchRequest request,
        CancellationToken cancellationToken);
}

public interface IGameMetadataRepository
{
    Task ApplyAsync(
        Game original,
        Game updated,
        MetadataCandidate candidate,
        CancellationToken cancellationToken);

    Task<Game?> UndoLastAsync(Guid gameId, CancellationToken cancellationToken);
}
