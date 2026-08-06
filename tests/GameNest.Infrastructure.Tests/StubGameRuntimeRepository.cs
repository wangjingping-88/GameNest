using GameNest.Application;
using GameNest.Domain;

namespace GameNest.Infrastructure.Tests;

internal sealed class StubGameRuntimeRepository : IGameRuntimeRepository
{
    public Task StartSessionAsync(PlaySession session, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task UpdateTrackedProcessIdsAsync(
        Guid sessionId,
        IReadOnlyCollection<int> processIds,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<PlaySession?> CompleteSessionAsync(
        Guid sessionId,
        DateTimeOffset endedAtUtc,
        GameExitKind exitKind,
        CancellationToken cancellationToken) =>
        Task.FromResult<PlaySession?>(null);

    public Task<IReadOnlyList<PlaySession>> GetSessionsAsync(
        Guid gameId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlaySession>>([]);

    public Task<IReadOnlyList<PlaySession>> GetActiveSessionsAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlaySession>>([]);
}
