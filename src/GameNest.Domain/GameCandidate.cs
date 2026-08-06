namespace GameNest.Domain;

public sealed record GameCandidate
{
    public GameCandidate(
        Guid id,
        Guid? scanRootId,
        string adapterId,
        GameCandidateSource source,
        string? sourceGameId,
        string title,
        string executablePath,
        string? arguments,
        string workingDirectory,
        string installRoot,
        string? volumeIdentity,
        string fingerprint,
        int score,
        IReadOnlyList<GameCandidateEvidence> evidence,
        string groupKey,
        bool isPrimary,
        GameCandidateDecision decision,
        DateTimeOffset discoveredAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("候选 ID 不能为空。", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(adapterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupKey);
        ArgumentNullException.ThrowIfNull(evidence);

        Id = id;
        ScanRootId = scanRootId;
        AdapterId = adapterId.Trim();
        Source = source;
        SourceGameId = string.IsNullOrWhiteSpace(sourceGameId) ? null : sourceGameId.Trim();
        Title = title.Trim();
        ExecutablePath = executablePath;
        Arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();
        WorkingDirectory = workingDirectory;
        InstallRoot = installRoot;
        VolumeIdentity = string.IsNullOrWhiteSpace(volumeIdentity) ? null : volumeIdentity.Trim();
        Fingerprint = fingerprint;
        Score = score;
        Evidence = evidence.ToArray();
        GroupKey = groupKey;
        IsPrimary = isPrimary;
        Decision = decision;
        DiscoveredAtUtc = discoveredAtUtc;
    }

    public Guid Id { get; }

    public Guid? ScanRootId { get; }

    public string AdapterId { get; }

    public GameCandidateSource Source { get; }

    public string? SourceGameId { get; }

    public string Title { get; }

    public string ExecutablePath { get; }

    public string? Arguments { get; }

    public string WorkingDirectory { get; }

    public string InstallRoot { get; }

    public string? VolumeIdentity { get; }

    public string Fingerprint { get; }

    public int Score { get; }

    public IReadOnlyList<GameCandidateEvidence> Evidence { get; }

    public string GroupKey { get; }

    public bool IsPrimary { get; }

    public GameCandidateDecision Decision { get; }

    public DateTimeOffset DiscoveredAtUtc { get; }

    public GameCandidateConfidence Confidence => Score switch
    {
        >= 70 => GameCandidateConfidence.High,
        >= 40 => GameCandidateConfidence.Medium,
        _ => GameCandidateConfidence.Ignored,
    };

    public GameCandidate WithGrouping(string groupKey, bool isPrimary) =>
        new(
            Id,
            ScanRootId,
            AdapterId,
            Source,
            SourceGameId,
            Title,
            ExecutablePath,
            Arguments,
            WorkingDirectory,
            InstallRoot,
            VolumeIdentity,
            Fingerprint,
            Score,
            Evidence,
            groupKey,
            isPrimary,
            Decision,
            DiscoveredAtUtc);

    public GameCandidate WithDecision(GameCandidateDecision decision) =>
        new(
            Id,
            ScanRootId,
            AdapterId,
            Source,
            SourceGameId,
            Title,
            ExecutablePath,
            Arguments,
            WorkingDirectory,
            InstallRoot,
            VolumeIdentity,
            Fingerprint,
            Score,
            Evidence,
            GroupKey,
            IsPrimary,
            decision,
            DiscoveredAtUtc);
}
