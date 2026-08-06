using GameNest.Application;
using GameNest.Domain;

namespace GameNest.Application.Tests;

internal sealed class MemoryGameRuntimeRepository : IGameRuntimeRepository
{
    private readonly Dictionary<Guid, PlaySession> _sessions = [];

    public Task StartSessionAsync(PlaySession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessions.Add(session.Id, session);
        return Task.CompletedTask;
    }

    public Task UpdateTrackedProcessIdsAsync(
        Guid sessionId,
        IReadOnlyCollection<int> processIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessions[sessionId] = _sessions[sessionId].WithTrackedProcessIds(processIds);
        return Task.CompletedTask;
    }

    public Task<PlaySession?> CompleteSessionAsync(
        Guid sessionId,
        DateTimeOffset endedAtUtc,
        GameExitKind exitKind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<PlaySession?>(null);
        }

        var completed = session.IsActive ? session.Complete(endedAtUtc, exitKind) : session;
        _sessions[sessionId] = completed;
        return Task.FromResult<PlaySession?>(completed);
    }

    public Task<IReadOnlyList<PlaySession>> GetSessionsAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PlaySession>>(
            _sessions.Values.Where(session => session.GameId == gameId).ToArray());
    }

    public Task<IReadOnlyList<PlaySession>> GetActiveSessionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PlaySession>>(
            _sessions.Values.Where(static session => session.IsActive).ToArray());
    }
}
