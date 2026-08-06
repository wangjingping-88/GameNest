using System.IO.Compression;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameNest.Application;
using GameNest.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GameNest.Infrastructure.Maintenance;

public sealed partial class LocalApplicationMaintenanceService(
    GameNestDataPaths paths,
    IApplicationDataInitializer dataInitializer,
    ILogger<LocalApplicationMaintenanceService> logger) : IApplicationMaintenanceService, IDisposable
{
    private const int MaximumAutomaticBackups = 7;
    private const int MaximumDiagnosticLogs = 3;
    private const int MaximumDiagnosticLogCharacters = 512 * 1024;
    private static readonly TimeSpan AutomaticBackupInterval = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions DiagnosticsJsonOptions =
        new() { WriteIndented = true };

    private static readonly Action<ILogger, string, Exception?> BackupCreated =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1500, nameof(BackupCreated)),
            "已创建本地数据库备份：{BackupFileName}。");

    private static readonly Action<ILogger, int, long, Exception?> CacheCleaned =
        LoggerMessage.Define<int, long>(
            LogLevel.Information,
            new EventId(1501, nameof(CacheCleaned)),
            "已清理 {DeletedFileCount} 个无引用图片缓存，回收 {ReclaimedBytes} 字节。");

    private static readonly Action<ILogger, string, Exception?> DiagnosticsExported =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1502, nameof(DiagnosticsExported)),
            "已导出脱敏诊断信息：{ArchiveFileName}。");

    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _disposed;

    public async Task<DataBackupResult> CreateAutomaticBackupAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await dataInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await Task.Run(
                    () => CreateAutomaticBackup(cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<DataBackupResult> CreateManualBackupAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await dataInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await Task.Run(
                    () => CreateBackup(paths.BackupDirectory, "manual", pruneAutomaticBackups: false, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<CacheCleanupResult> CleanupImageCacheAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await dataInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await Task.Run(
                    () => CleanupImageCache(cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            CacheCleaned(logger, result.DeletedFileCount, result.ReclaimedBytes, null);
            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<DiagnosticsExportResult> ExportDiagnosticsAsync(
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        await dataInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await Task.Run(
                    () => ExportDiagnostics(Path.GetFullPath(destinationDirectory), cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            DiagnosticsExported(logger, Path.GetFileName(result.ArchiveFile), null);
            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _operationGate.Dispose();
        _disposed = true;
    }

    private DataBackupResult CreateAutomaticBackup(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(paths.BackupDirectory);
        var latestBackup = new DirectoryInfo(paths.BackupDirectory)
            .EnumerateFiles("gamenest-auto-*.db", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (latestBackup is not null && DateTime.UtcNow - latestBackup.LastWriteTimeUtc < AutomaticBackupInterval)
        {
            return new DataBackupResult(false, latestBackup.FullName, 0);
        }

        return CreateBackup(paths.BackupDirectory, "auto", pruneAutomaticBackups: true, cancellationToken);
    }

    private DataBackupResult CreateBackup(
        string destinationDirectory,
        string kind,
        bool pruneAutomaticBackups,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationDirectory);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(destinationDirectory, $"gamenest-{kind}-{timestamp}.db");
        var temporaryPath = backupPath + ".partial";

        try
        {
            using var source = SqliteConnectionFactory.Create(paths);
            source.Open();
            using var destination = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = temporaryPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Private,
                    Pooling = false,
                }.ToString());
            destination.Open();
            source.BackupDatabase(destination);
            destination.Close();
            File.Move(temporaryPath, backupPath);
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }

        var prunedCount = pruneAutomaticBackups
            ? PruneAutomaticBackups(cancellationToken)
            : 0;
        BackupCreated(logger, Path.GetFileName(backupPath), null);
        return new DataBackupResult(true, backupPath, prunedCount);
    }

    private int PruneAutomaticBackups(CancellationToken cancellationToken)
    {
        var staleBackups = new DirectoryInfo(paths.BackupDirectory)
            .EnumerateFiles("gamenest-auto-*.db", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .Skip(MaximumAutomaticBackups)
            .ToArray();

        foreach (var staleBackup in staleBackups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            staleBackup.Delete();
        }

        return staleBackups.Length;
    }

    private CacheCleanupResult CleanupImageCache(CancellationToken cancellationToken)
    {
        var referencedPaths = GetReferencedAssetPaths(cancellationToken);
        var deletedFileCount = 0;
        long reclaimedBytes = 0;

        foreach (var cacheDirectory in paths.ImageCacheDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(cacheDirectory))
            {
                continue;
            }

            var fullCacheDirectory = Path.GetFullPath(cacheDirectory);
            foreach (var filePath in Directory.EnumerateFiles(fullCacheDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullFilePath = Path.GetFullPath(filePath);
                if (!IsDirectChildOf(fullFilePath, fullCacheDirectory) || referencedPaths.Contains(fullFilePath))
                {
                    continue;
                }

                var length = new FileInfo(fullFilePath).Length;
                File.Delete(fullFilePath);
                deletedFileCount++;
                reclaimedBytes += length;
            }
        }

        return new CacheCleanupResult(deletedFileCount, reclaimedBytes);
    }

    private HashSet<string> GetReferencedAssetPaths(CancellationToken cancellationToken)
    {
        using var connection = SqliteConnectionFactory.Create(paths);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT LocalPath FROM GameAssets;";
        using var reader = command.ExecuteReader();
        var referencedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localPath = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                referencedPaths.Add(Path.GetFullPath(localPath));
            }
        }

        return referencedPaths;
    }

    private DiagnosticsExportResult ExportDiagnostics(
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationDirectory);
        var archivePath = ResolveUniquePath(
            destinationDirectory,
            $"GameNest-diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip");

        try
        {
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            WriteDiagnosticsSummary(archive, cancellationToken);
            WriteSanitizedLogs(archive, cancellationToken);
        }
        catch
        {
            TryDeleteFile(archivePath);
            throw;
        }

        return new DiagnosticsExportResult(archivePath, new FileInfo(archivePath).Length);
    }

    private void WriteDiagnosticsSummary(ZipArchive archive, CancellationToken cancellationToken)
    {
        var databaseCounts = ReadDatabaseCounts(cancellationToken);
        var cacheFiles = paths.ImageCacheDirectories
            .Where(Directory.Exists)
            .SelectMany(static directory => Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            .Select(static path => new FileInfo(path))
            .ToArray();
        var backups = Directory.Exists(paths.BackupDirectory)
            ? Directory.EnumerateFiles(paths.BackupDirectory, "*.db", SearchOption.TopDirectoryOnly)
                .Select(static path => new FileInfo(path))
                .ToArray()
            : [];
        var entryAssembly = Assembly.GetEntryAssembly() ?? typeof(LocalApplicationMaintenanceService).Assembly;
        var summary = new
        {
            exportedAtUtc = DateTimeOffset.UtcNow,
            appVersion = entryAssembly.GetName().Version?.ToString() ?? "unknown",
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            database = new
            {
                exists = File.Exists(paths.DatabaseFile),
                bytes = File.Exists(paths.DatabaseFile) ? new FileInfo(paths.DatabaseFile).Length : 0,
                databaseCounts.Migrations,
                databaseCounts.Games,
                databaseCounts.ScanRoots,
                databaseCounts.PlaySessions,
                databaseCounts.GameAssets,
            },
            imageCache = new
            {
                fileCount = cacheFiles.Length,
                bytes = cacheFiles.Sum(static file => file.Length),
            },
            backups = new
            {
                count = backups.Length,
                latestWriteUtc = backups.Length == 0
                    ? (DateTimeOffset?)null
                    : backups.Max(static file => new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)),
                automaticRetention = MaximumAutomaticBackups,
            },
            privacy = "不包含游戏标题、安装路径、数据库内容或凭据；日志已脱敏。",
        };

        var entry = archive.CreateEntry("diagnostics.json", CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, summary, DiagnosticsJsonOptions);
    }

    private DatabaseCounts ReadDatabaseCounts(CancellationToken cancellationToken)
    {
        using var connection = SqliteConnectionFactory.Create(paths);
        connection.Open();
        return new DatabaseCounts(
            ReadCount(connection, "SchemaMigrations", cancellationToken),
            ReadCount(connection, "Games", cancellationToken),
            ReadCount(connection, "ScanRoots", cancellationToken),
            ReadCount(connection, "PlaySessions", cancellationToken),
            ReadCount(connection, "GameAssets", cancellationToken));
    }

    private static long ReadCount(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private void WriteSanitizedLogs(ZipArchive archive, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(paths.LogDirectory))
        {
            return;
        }

        var logFiles = new DirectoryInfo(paths.LogDirectory)
            .EnumerateFiles("*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .Take(MaximumDiagnosticLogs)
            .ToArray();

        foreach (var logFile in logFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var source = new FileStream(
                logFile.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            if (text.Length > MaximumDiagnosticLogCharacters)
            {
                text = text[^MaximumDiagnosticLogCharacters..];
            }

            var entry = archive.CreateEntry($"logs/{logFile.Name}", CompressionLevel.Optimal);
            using var target = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            target.Write(RedactSensitiveData(text));
        }
    }

    private string RedactSensitiveData(string value)
    {
        var redacted = value.Replace(paths.RootDirectory, "<GameNestData>", StringComparison.OrdinalIgnoreCase);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            redacted = redacted.Replace(userProfile, "<UserProfile>", StringComparison.OrdinalIgnoreCase);
        }

        redacted = WindowsPathRegex().Replace(redacted, "<本地路径>");
        return SecretAssignmentRegex().Replace(redacted, static match => $"{match.Groups[1].Value}=<已移除>");
    }

    private static bool IsDirectChildOf(string filePath, string directoryPath) =>
        string.Equals(
            Path.GetDirectoryName(filePath)?.TrimEnd(Path.DirectorySeparatorChar),
            directoryPath.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string ResolveUniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var sequence = 2; ; sequence++)
        {
            candidate = Path.Combine(directory, $"{stem}-{sequence}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex(@"(?i)\b(api[_-]?key|access[_-]?token|secret)\b\s*[:=]\s*\S+")]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(@"(?i)\b[A-Z]:\\(?:[^\s\r\n\""']+\\)*[^\s\r\n\""']*")]
    private static partial Regex WindowsPathRegex();

    private sealed record DatabaseCounts(
        long Migrations,
        long Games,
        long ScanRoots,
        long PlaySessions,
        long GameAssets);
}
