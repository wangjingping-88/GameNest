namespace GameNest.Application;

public sealed record DataBackupResult(
    bool Created,
    string BackupFile,
    int PrunedBackupCount);

public sealed record CacheCleanupResult(
    int DeletedFileCount,
    long ReclaimedBytes);

public sealed record DiagnosticsExportResult(
    string ArchiveFile,
    long ArchiveBytes);

public interface IApplicationMaintenanceService
{
    Task<DataBackupResult> CreateAutomaticBackupAsync(CancellationToken cancellationToken);

    Task<DataBackupResult> CreateManualBackupAsync(CancellationToken cancellationToken);

    Task<CacheCleanupResult> CleanupImageCacheAsync(CancellationToken cancellationToken);

    Task<DiagnosticsExportResult> ExportDiagnosticsAsync(
        string destinationDirectory,
        CancellationToken cancellationToken);
}
