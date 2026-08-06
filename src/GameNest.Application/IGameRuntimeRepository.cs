using GameNest.Domain;

namespace GameNest.Application;

public interface IGameRuntimeRepository
{
    Task StartSessionAsync(PlaySession session, CancellationToken cancellationToken);

    Task UpdateTrackedProcessIdsAsync(
        Guid sessionId,
        IReadOnlyCollection<int> processIds,
        CancellationToken cancellationToken);

    Task<PlaySession?> CompleteSessionAsync(
        Guid sessionId,
        DateTimeOffset endedAtUtc,
        GameExitKind exitKind,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlaySession>> GetSessionsAsync(
        Guid gameId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlaySession>> GetActiveSessionsAsync(CancellationToken cancellationToken);
}
