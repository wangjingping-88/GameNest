namespace GameNest.Application;

public enum UpdateAvailability
{
    NotChecked,
    UpToDate,
    Available,
    Unavailable,
}

public enum UpdateInstallCapability
{
    Ready,
    TrustedSigningKeyUnavailable,
    NotPortable,
    ProgramDirectoryNotWritable,
    UnsupportedPlatform,
}

public enum UpdateOperationStage
{
    DownloadingManifest,
    VerifyingManifest,
    DownloadingPackage,
    VerifyingPackage,
    ExtractingPackage,
    PreparingInstaller,
    ReadyToInstall,
}

public sealed record UpdateReleaseAsset(
    string Name,
    Uri DownloadUri,
    long SizeBytes,
    string? GitHubDigest);

public sealed record UpdateRelease(
    Version Version,
    string TagName,
    string Title,
    string ReleaseNotes,
    Uri ReleasePageUri,
    DateTimeOffset PublishedAtUtc,
    UpdateReleaseAsset Package,
    UpdateReleaseAsset Manifest,
    UpdateReleaseAsset Signature);

public sealed record UpdatePreference(
    bool AutomaticCheckEnabled,
    DateTimeOffset? LastCheckedUtc,
    string? EntityTag)
{
    public static UpdatePreference Default { get; } = new(true, null, null);
}

public sealed record UpdateCheckResult(
    UpdateAvailability Availability,
    Version CurrentVersion,
    UpdateRelease? Release,
    UpdateInstallCapability InstallCapability,
    DateTimeOffset CheckedAtUtc,
    string Message);

public sealed record UpdateProgress(
    UpdateOperationStage Stage,
    long CompletedBytes,
    long? TotalBytes,
    string Message)
{
    public double? Percent => TotalBytes is > 0
        ? Math.Clamp(CompletedBytes * 100d / TotalBytes.Value, 0d, 100d)
        : null;
}

public sealed record PreparedApplicationUpdate(
    UpdateRelease Release,
    string InstallerExecutable,
    string PlanFile,
    string StagingDirectory,
    string DatabaseBackupFile);

public sealed record UpdateLaunchResult(
    bool Started,
    string Message);

public interface IUpdatePreferenceStore
{
    Task<UpdatePreference> GetAsync(CancellationToken cancellationToken);

    Task SetAsync(UpdatePreference preference, CancellationToken cancellationToken);
}

public interface IApplicationUpdateService
{
    Version CurrentVersion { get; }

    Task<UpdatePreference> GetPreferenceAsync(CancellationToken cancellationToken);

    Task SetAutomaticCheckEnabledAsync(bool enabled, CancellationToken cancellationToken);

    Task<UpdateCheckResult> CheckAsync(bool force, CancellationToken cancellationToken);

    Task<PreparedApplicationUpdate> PrepareAsync(
        UpdateRelease release,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken);

    Task<UpdateLaunchResult> LaunchInstallerAsync(
        PreparedApplicationUpdate preparedUpdate,
        int currentProcessId,
        CancellationToken cancellationToken);
}

public interface IPortableUpdateApplier
{
    Task<int> ApplyAsync(string planFile, CancellationToken cancellationToken);
}
