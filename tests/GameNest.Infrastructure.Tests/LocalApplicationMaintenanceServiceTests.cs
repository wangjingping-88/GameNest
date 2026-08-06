using System.IO.Compression;
using GameNest.Infrastructure;
using GameNest.Infrastructure.Maintenance;
using GameNest.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class LocalApplicationMaintenanceServiceTests
{
    [Fact]
    public async Task AutomaticBackupCreatesConsistentSnapshotAndThrottlesSameDay()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = CreateInitializer(paths);
        using var service = CreateService(paths, initializer);

        await initializer.InitializeAsync(TestContext.Current.CancellationToken);
        var first = await service.CreateAutomaticBackupAsync(TestContext.Current.CancellationToken);
        var second = await service.CreateAutomaticBackupAsync(TestContext.Current.CancellationToken);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.BackupFile, second.BackupFile);
        Assert.True(File.Exists(first.BackupFile));
        await using var backup = SqliteDatabaseInitializerTests.CreateConnection(
            GameNestDataPaths.CreateForRoot(Path.GetDirectoryName(Path.GetDirectoryName(first.BackupFile)!)!));
        backup.ConnectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = first.BackupFile,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
        }.ToString();
        await backup.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = backup.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SchemaMigrations;";
        Assert.Equal(4L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CleanupImageCacheDeletesOnlyUnreferencedDirectChildren()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = CreateInitializer(paths);
        using var service = CreateService(paths, initializer);
        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        var cacheDirectory = paths.ImageCacheDirectories[0];
        Directory.CreateDirectory(cacheDirectory);
        var referenced = Path.Combine(cacheDirectory, "referenced.png");
        var orphaned = Path.Combine(cacheDirectory, "orphaned.png");
        await File.WriteAllBytesAsync(referenced, [1, 2, 3], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(orphaned, [4, 5, 6, 7], TestContext.Current.CancellationToken);
        await AddReferencedAssetAsync(paths, referenced, TestContext.Current.CancellationToken);

        var result = await service.CleanupImageCacheAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.DeletedFileCount);
        Assert.Equal(4, result.ReclaimedBytes);
        Assert.True(File.Exists(referenced));
        Assert.False(File.Exists(orphaned));
    }

    [Fact]
    public async Task ExportDiagnosticsOmitsDatabaseContentAndRedactsPathsAndSecrets()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = CreateInitializer(paths);
        using var service = CreateService(paths, initializer);
        await initializer.InitializeAsync(TestContext.Current.CancellationToken);
        Directory.CreateDirectory(paths.LogDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(paths.LogDirectory, "gamenest-test.log"),
            @"读取 C:\Users\Alice\Games\example.exe 失败；API_KEY=secret123",
            TestContext.Current.CancellationToken);
        var exportDirectory = Path.Combine(directory.Path, "exports");

        var result = await service.ExportDiagnosticsAsync(
            exportDirectory,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(result.ArchiveFile));
        using var archive = ZipFile.OpenRead(result.ArchiveFile);
        Assert.Contains(archive.Entries, static entry => entry.FullName == "diagnostics.json");
        Assert.DoesNotContain(archive.Entries, static entry => entry.FullName.EndsWith(".db", StringComparison.OrdinalIgnoreCase));
        var logEntry = Assert.Single(archive.Entries, static entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal));
        using var reader = new StreamReader(logEntry.Open());
        var sanitizedLog = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(@"C:\Users\Alice", sanitizedLog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret123", sanitizedLog, StringComparison.Ordinal);
        Assert.Contains("<本地路径>", sanitizedLog, StringComparison.Ordinal);
        Assert.Contains("<已移除>", sanitizedLog, StringComparison.Ordinal);
    }

    private static SqliteDatabaseInitializer CreateInitializer(GameNestDataPaths paths) =>
        new(paths, NullLogger<SqliteDatabaseInitializer>.Instance);

    private static LocalApplicationMaintenanceService CreateService(
        GameNestDataPaths paths,
        SqliteDatabaseInitializer initializer) =>
        new(paths, initializer, NullLogger<LocalApplicationMaintenanceService>.Instance);

    private static async Task AddReferencedAssetAsync(
        GameNestDataPaths paths,
        string localPath,
        CancellationToken cancellationToken)
    {
        await using var connection = SqliteDatabaseInitializerTests.CreateConnection(paths);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Games (
                Id, Title, SortTitle, Description, InstallRoot, SourceType, SourceGameId,
                VolumeIdentity, IsFavorite, IsHidden, Availability, DetectionConfidence,
                UserEditedFields, DateAddedUtc, LastPlayedUtc, TotalPlaySeconds)
            VALUES (
                $gameId, 'Test Game', 'Test Game', NULL, $root, 'Manual', NULL,
                NULL, 0, 0, 'Available', 100, '[]', $now, NULL, 0);
            INSERT INTO GameAssets (
                Id, GameId, AssetType, LocalPath, Source, Width, Height, ContentHash, UpdatedAtUtc)
            VALUES (
                $assetId, $gameId, 'Cover', $path, 'User', 1, 1, NULL, $now);
            """;
        command.Parameters.AddWithValue("$gameId", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$assetId", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$root", paths.RootDirectory);
        command.Parameters.AddWithValue("$path", localPath);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
