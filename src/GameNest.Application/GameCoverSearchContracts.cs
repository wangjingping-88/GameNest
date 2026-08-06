using GameNest.Domain;

namespace GameNest.Application;

public sealed record GameCoverCandidate(
    string Title,
    Uri ImageUri,
    string SourceName,
    bool IsExactTitleMatch);

public interface IGameCoverSearchProvider
{
    Task<IReadOnlyList<GameCoverCandidate>> SearchAsync(
        string title,
        CancellationToken cancellationToken);
}
