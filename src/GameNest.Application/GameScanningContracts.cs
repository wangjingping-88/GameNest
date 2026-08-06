using GameNest.Domain;

namespace GameNest.Application;

public sealed record DiscoveredGame(
    Guid? ScanRootId,
    string AdapterId,
    GameCandidateSource Source,
    string? SourceGameId,
    string Title,
    string ExecutablePath,
    string? Arguments,
    string WorkingDirectory,
    string InstallRoot,
    string? VolumeIdentity,
    long FileSize,
    DateTimeOffset LastWriteUtc,
    IReadOnlyList<GameCandidateEvidence> Evidence,
    GameCandidate? PreviousCandidate = null);

public sealed record GameScanContext(
    ScanMode Mode,
    IReadOnlyList<ScanRoot> Roots,
    IReadOnlyList<string> ExcludedDirectories,
    IReadOnlyDictionary<string, GameCandidate> PreviousCandidates,
    IScanPauseToken PauseToken);

public sealed record GameScanProgress(
    string Stage,
    string? CurrentPath,
    long CheckedDirectoryCount,
    long CandidateCount,
    TimeSpan Elapsed);

public sealed record GameScanSummary(
    Guid RunId,
    ScanMode Mode,
    int CandidateCount,
    long CheckedDirectoryCount,
    TimeSpan Elapsed,
    bool WasCancelled);

public enum GameScanRunStatus
{
    Running,
    Completed,
    Cancelled,
    Failed,
}

public interface IScanPauseToken
{
    bool IsPaused { get; }

    Task WaitWhilePausedAsync(CancellationToken cancellationToken);
}

public interface IGameSourceAdapter
{
    string Id { get; }

    Task<IReadOnlyList<DiscoveredGame>> ScanAsync(
        GameScanContext context,
        IProgress<GameScanProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IGameCandidateScorer
{
    GameCandidate Score(DiscoveredGame discovery, DateTimeOffset discoveredAtUtc);
}

public interface IGameCandidateGrouper
{
    IReadOnlyList<GameCandidate> Group(IReadOnlyList<GameCandidate> candidates);
}

public interface IGameScanRepository
{
    Task<IReadOnlyList<ScanRoot>> GetRootsAsync(CancellationToken cancellationToken);

    Task AddRootAsync(ScanRoot root, CancellationToken cancellationToken);

    Task UpdateRootAsync(ScanRoot root, CancellationToken cancellationToken);

    Task<bool> RemoveRootAsync(Guid rootId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GameCandidate>> GetCandidatesAsync(CancellationToken cancellationToken);

    Task<GameCandidate?> GetCandidateAsync(Guid candidateId, CancellationToken cancellationToken);

    Task<Guid> StartRunAsync(ScanMode mode, CancellationToken cancellationToken);

    Task SaveCandidatesAsync(
        Guid runId,
        IReadOnlyList<GameCandidate> candidates,
        CancellationToken cancellationToken);

    Task CompleteRunAsync(
        Guid runId,
        GameScanRunStatus status,
        long checkedDirectoryCount,
        int candidateCount,
        string? errorMessage,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetExcludedDirectoriesAsync(CancellationToken cancellationToken);

    Task AddExcludedDirectoryAsync(string path, CancellationToken cancellationToken);

    Task<string?> UndoLastExcludedDirectoryAsync(CancellationToken cancellationToken);

    Task SetCandidateDecisionAsync(
        Guid candidateId,
        GameCandidateDecision decision,
        CancellationToken cancellationToken);
}

public sealed record VolumeLocation(
    string Identity,
    string VolumeRoot,
    string CurrentPath,
    string RelativePath,
    bool IsOnline);

public interface IVolumeIdentityService
{
    Task<VolumeLocation> ResolveAsync(string path, CancellationToken cancellationToken);

    Task<VolumeLocation?> FindAsync(
        string volumeIdentity,
        string relativePath,
        CancellationToken cancellationToken);
}
