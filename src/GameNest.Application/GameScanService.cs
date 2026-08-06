using GameNest.Domain;

namespace GameNest.Application;

public sealed class GameScanService(
    IEnumerable<IGameSourceAdapter> adapters,
    IGameCandidateScorer scorer,
    IGameCandidateGrouper grouper,
    IGameScanRepository scanRepository,
    IVolumeIdentityService volumeIdentityService,
    IGameLibraryRepository gameLibraryRepository,
    GameLibraryService gameLibraryService)
{
    private readonly IReadOnlyList<IGameSourceAdapter> _adapters = adapters.ToArray();

    public async Task<IReadOnlyList<ScanRoot>> GetRootsAsync(CancellationToken cancellationToken)
    {
        var roots = await scanRepository.GetRootsAsync(cancellationToken).ConfigureAwait(false);
        var refreshed = new List<ScanRoot>(roots.Count);
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = await volumeIdentityService
                .FindAsync(root.VolumeIdentity, root.RelativePath, cancellationToken)
                .ConfigureAwait(false);
            var updated = location is null
                ? root.WithLocation(root.CurrentPath, isOnline: false)
                : root.WithLocation(location.CurrentPath, location.IsOnline);

            if (updated.CurrentPath != root.CurrentPath || updated.IsOnline != root.IsOnline)
            {
                await scanRepository.UpdateRootAsync(updated, cancellationToken).ConfigureAwait(false);
                if (updated.IsOnline && !PathEquals(root.CurrentPath, updated.CurrentPath))
                {
                    await gameLibraryRepository
                        .RebindVolumeAsync(root.VolumeIdentity, root.CurrentPath, updated.CurrentPath, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            await gameLibraryRepository
                .SetAvailabilityByVolumeAsync(
                    root.VolumeIdentity,
                    updated.IsOnline ? GameAvailability.Available : GameAvailability.VolumeOffline,
                    cancellationToken)
                .ConfigureAwait(false);
            refreshed.Add(updated);
        }

        return refreshed;
    }

    public async Task<ScanRoot> AddRootAsync(
        string path,
        ScanMode scanMode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var location = await volumeIdentityService.ResolveAsync(path, cancellationToken).ConfigureAwait(false);
        var roots = await scanRepository.GetRootsAsync(cancellationToken).ConfigureAwait(false);
        var existing = roots.FirstOrDefault(
            root => PathEquals(root.CurrentPath, location.CurrentPath));
        if (existing is not null)
        {
            throw new InvalidOperationException("该目录已经在扫描范围中。");
        }

        var root = new ScanRoot(
            Guid.NewGuid(),
            location.Identity,
            location.CurrentPath,
            location.RelativePath,
            scanMode,
            isEnabled: true,
            isOnline: true,
            lastScanUtc: null,
            lastCheckpoint: null);
        await scanRepository.AddRootAsync(root, cancellationToken).ConfigureAwait(false);
        return root;
    }

    public Task<bool> RemoveRootAsync(Guid rootId, CancellationToken cancellationToken) =>
        scanRepository.RemoveRootAsync(rootId, cancellationToken);

    public async Task SetRootEnabledAsync(
        Guid rootId,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var root = (await scanRepository.GetRootsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Id == rootId)
            ?? throw new KeyNotFoundException("找不到扫描根目录。");
        await scanRepository.UpdateRootAsync(
            new ScanRoot(
                root.Id,
                root.VolumeIdentity,
                root.CurrentPath,
                root.RelativePath,
                root.ScanMode,
                isEnabled,
                root.IsOnline,
                root.LastScanUtc,
                root.LastCheckpoint),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<GameCandidate>> GetCandidatesAsync(CancellationToken cancellationToken) =>
        scanRepository.GetCandidatesAsync(cancellationToken);

    public async Task<GameScanSummary> RunAsync(
        ScanMode mode,
        IScanPauseToken pauseToken,
        IProgress<GameScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pauseToken);
        var startedAt = DateTimeOffset.UtcNow;
        var roots = (await GetRootsAsync(cancellationToken).ConfigureAwait(false))
            .Where(root => root.IsEnabled && root.IsOnline && (mode == ScanMode.Deep || root.ScanMode == ScanMode.Quick))
            .ToArray();
        if (roots.Length == 0)
        {
            throw new InvalidOperationException("请先添加至少一个在线的扫描目录。");
        }

        var previous = await scanRepository.GetCandidatesAsync(cancellationToken).ConfigureAwait(false);
        var previousByPath = previous
            .GroupBy(static candidate => candidate.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var exclusions = await scanRepository
            .GetExcludedDirectoriesAsync(cancellationToken)
            .ConfigureAwait(false);
        var context = new GameScanContext(mode, roots, exclusions, previousByPath, pauseToken);
        var runId = await scanRepository.StartRunAsync(mode, cancellationToken).ConfigureAwait(false);
        var progressRelay = new ProgressRelay(progress);

        try
        {
            var adapterResults = await Task.WhenAll(
                    _adapters.Select(adapter => ScanAdapterSafeAsync(
                        adapter,
                        context,
                        progressRelay,
                        cancellationToken)))
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var scored = adapterResults
                .SelectMany(static result => result.Candidates)
                .Select(discovery => scorer.Score(discovery, DateTimeOffset.UtcNow))
                .GroupBy(static candidate => candidate.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenByDescending(candidate => SourcePriority(candidate.Source))
                    .First())
                .ToArray();
            var grouped = grouper.Group(scored);
            await scanRepository.SaveCandidatesAsync(runId, grouped, cancellationToken).ConfigureAwait(false);

            var checkpoint = DateTimeOffset.UtcNow.ToString("O");
            foreach (var root in roots)
            {
                await scanRepository
                    .UpdateRootAsync(root.WithCheckpoint(DateTimeOffset.UtcNow, checkpoint), cancellationToken)
                    .ConfigureAwait(false);
            }

            var elapsed = DateTimeOffset.UtcNow - startedAt;
            await scanRepository
                .CompleteRunAsync(
                    runId,
                    GameScanRunStatus.Completed,
                    progressRelay.CheckedDirectoryCount,
                    grouped.Count,
                    string.Join(
                        Environment.NewLine,
                        adapterResults
                            .Where(static result => result.ErrorMessage is not null)
                            .Select(static result => result.ErrorMessage)),
                    cancellationToken)
                .ConfigureAwait(false);
            return new GameScanSummary(
                runId,
                mode,
                grouped.Count,
                progressRelay.CheckedDirectoryCount,
                elapsed,
                WasCancelled: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await scanRepository
                .CompleteRunAsync(
                    runId,
                    GameScanRunStatus.Cancelled,
                    progressRelay.CheckedDirectoryCount,
                    ToInt(progressRelay.CandidateCount),
                    errorMessage: null,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return new GameScanSummary(
                runId,
                mode,
                ToInt(progressRelay.CandidateCount),
                progressRelay.CheckedDirectoryCount,
                DateTimeOffset.UtcNow - startedAt,
                WasCancelled: true);
        }
        catch (Exception exception)
        {
            await scanRepository
                .CompleteRunAsync(
                    runId,
                    GameScanRunStatus.Failed,
                    progressRelay.CheckedDirectoryCount,
                    ToInt(progressRelay.CandidateCount),
                    exception.Message,
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    public async Task<int> ConfirmAsync(
        IReadOnlyCollection<Guid> candidateIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidateIds);
        var imported = 0;
        foreach (var candidateId in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = await scanRepository
                .GetCandidateAsync(candidateId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new KeyNotFoundException("找不到待确认的扫描候选。");
            await gameLibraryService.ImportCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
            await scanRepository
                .SetCandidateDecisionAsync(candidateId, GameCandidateDecision.Confirmed, cancellationToken)
                .ConfigureAwait(false);
            imported++;
        }

        return imported;
    }

    public async Task<string> ExcludeDirectoryAsync(
        Guid candidateId,
        CancellationToken cancellationToken)
    {
        var candidate = await scanRepository.GetCandidateAsync(candidateId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("找不到要排除的扫描候选。");
        await scanRepository
            .AddExcludedDirectoryAsync(candidate.InstallRoot, cancellationToken)
            .ConfigureAwait(false);
        await scanRepository
            .SetCandidateDecisionAsync(candidateId, GameCandidateDecision.Excluded, cancellationToken)
            .ConfigureAwait(false);
        return candidate.InstallRoot;
    }

    public Task<string?> UndoLastExclusionAsync(CancellationToken cancellationToken) =>
        scanRepository.UndoLastExcludedDirectoryAsync(cancellationToken);

    private static int SourcePriority(GameCandidateSource source) => source switch
    {
        GameCandidateSource.Steam => 3,
        GameCandidateSource.Shortcut => 2,
        _ => 1,
    };

    private static async Task<AdapterScanResult> ScanAdapterSafeAsync(
        IGameSourceAdapter adapter,
        GameScanContext context,
        IProgress<GameScanProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var candidates = await adapter
                .ScanAsync(context, progress, cancellationToken)
                .ConfigureAwait(false);
            return new AdapterScanResult(candidates, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            progress.Report(
                new GameScanProgress(
                    $"{adapter.Id} 已跳过",
                    null,
                    0,
                    0,
                    TimeSpan.Zero));
            return new AdapterScanResult([], $"{adapter.Id}: {exception.Message}");
        }
    }

    private static int ToInt(long value) =>
        value >= int.MaxValue ? int.MaxValue : (int)Math.Max(0, value);

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private sealed class ProgressRelay(IProgress<GameScanProgress>? inner) : IProgress<GameScanProgress>
    {
        private long _candidateCount;
        private long _checkedDirectoryCount;

        public long CandidateCount => Interlocked.Read(ref _candidateCount);

        public long CheckedDirectoryCount => Interlocked.Read(ref _checkedDirectoryCount);

        public void Report(GameScanProgress value)
        {
            InterlockedExtensions.Max(ref _candidateCount, value.CandidateCount);
            InterlockedExtensions.Max(ref _checkedDirectoryCount, value.CheckedDirectoryCount);
            inner?.Report(value);
        }
    }

    private sealed record AdapterScanResult(
        IReadOnlyList<DiscoveredGame> Candidates,
        string? ErrorMessage);

    private static class InterlockedExtensions
    {
        public static void Max(ref long target, long value)
        {
            var current = Interlocked.Read(ref target);
            while (value > current)
            {
                var original = Interlocked.CompareExchange(ref target, value, current);
                if (original == current)
                {
                    return;
                }

                current = original;
            }
        }
    }
}
