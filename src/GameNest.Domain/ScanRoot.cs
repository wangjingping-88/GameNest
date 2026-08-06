namespace GameNest.Domain;

public sealed record ScanRoot
{
    public ScanRoot(
        Guid id,
        string volumeIdentity,
        string currentPath,
        string relativePath,
        ScanMode scanMode,
        bool isEnabled,
        bool isOnline,
        DateTimeOffset? lastScanUtc,
        string? lastCheckpoint)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("扫描根目录 ID 不能为空。", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(volumeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPath);

        Id = id;
        VolumeIdentity = volumeIdentity.Trim();
        CurrentPath = currentPath;
        RelativePath = relativePath ?? string.Empty;
        ScanMode = scanMode;
        IsEnabled = isEnabled;
        IsOnline = isOnline;
        LastScanUtc = lastScanUtc;
        LastCheckpoint = string.IsNullOrWhiteSpace(lastCheckpoint) ? null : lastCheckpoint.Trim();
    }

    public Guid Id { get; }

    public string VolumeIdentity { get; }

    public string CurrentPath { get; }

    public string RelativePath { get; }

    public ScanMode ScanMode { get; }

    public bool IsEnabled { get; }

    public bool IsOnline { get; }

    public DateTimeOffset? LastScanUtc { get; }

    public string? LastCheckpoint { get; }

    public ScanRoot WithLocation(string currentPath, bool isOnline) =>
        new(Id, VolumeIdentity, currentPath, RelativePath, ScanMode, IsEnabled, isOnline, LastScanUtc, LastCheckpoint);

    public ScanRoot WithCheckpoint(DateTimeOffset scannedAtUtc, string checkpoint) =>
        new(Id, VolumeIdentity, CurrentPath, RelativePath, ScanMode, IsEnabled, IsOnline, scannedAtUtc, checkpoint);
}
