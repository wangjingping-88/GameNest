namespace GameNest.Domain;

public sealed record PlaySession
{
    public PlaySession(
        Guid id,
        Guid gameId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        long? durationSeconds,
        GameExitKind? exitKind,
        IReadOnlyCollection<int> trackedProcessIds)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("会话 ID 不能为空。", nameof(id));
        }

        if (gameId == Guid.Empty)
        {
            throw new ArgumentException("游戏 ID 不能为空。", nameof(gameId));
        }

        ArgumentNullException.ThrowIfNull(trackedProcessIds);
        if (trackedProcessIds.Any(static processId => processId <= 0))
        {
            throw new ArgumentException("跟踪的进程 ID 必须大于零。", nameof(trackedProcessIds));
        }

        if (endedAtUtc is not null && endedAtUtc < startedAtUtc)
        {
            throw new ArgumentException("会话结束时间不能早于开始时间。", nameof(endedAtUtc));
        }

        if ((endedAtUtc is null) != (durationSeconds is null) || (endedAtUtc is null) != (exitKind is null))
        {
            throw new ArgumentException("结束时间、持续秒数和退出类型必须同时为空或同时提供。", nameof(endedAtUtc));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(durationSeconds ?? 0);

        Id = id;
        GameId = gameId;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
        DurationSeconds = durationSeconds;
        ExitKind = exitKind;
        TrackedProcessIds = trackedProcessIds.Distinct().Order().ToArray();
    }

    public Guid Id { get; }

    public Guid GameId { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? EndedAtUtc { get; }

    public long? DurationSeconds { get; }

    public GameExitKind? ExitKind { get; }

    public IReadOnlyList<int> TrackedProcessIds { get; }

    public bool IsActive => EndedAtUtc is null;

    public PlaySession WithTrackedProcessIds(IReadOnlyCollection<int> processIds) =>
        new(Id, GameId, StartedAtUtc, EndedAtUtc, DurationSeconds, ExitKind, processIds);

    public PlaySession Complete(DateTimeOffset endedAtUtc, GameExitKind exitKind)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("会话已经结束。不能重复结算。");
        }

        var durationSeconds = Math.Max(0, (long)(endedAtUtc - StartedAtUtc).TotalSeconds);
        return new PlaySession(
            Id,
            GameId,
            StartedAtUtc,
            endedAtUtc,
            durationSeconds,
            exitKind,
            TrackedProcessIds);
    }
}
