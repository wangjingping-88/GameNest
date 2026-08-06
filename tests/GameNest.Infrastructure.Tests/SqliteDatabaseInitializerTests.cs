using GameNest.Application;
using GameNest.Infrastructure;
using GameNest.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class SqliteDatabaseInitializerTests
{
    private static readonly string[] ExpectedTables =
    [
        "AppSettings",
        "GameAssets",
        "Games",
        "LaunchProfiles",
        "MetadataRevisions",
        "OverlayProfiles",
        "PlaySessions",
        "ScanCandidates",
        "ScanExclusions",
        "ScanRoots",
        "ScanRuns",
        "SchemaMigrations",
    ];

    [Fact]
    public async Task InitializeAsyncCreatesSchemaAndEnablesWal()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = CreateInitializer(paths);

        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        await using var connection = CreateConnection(paths);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var tableNames = await GetTableNamesAsync(connection, TestContext.Current.CancellationToken);
        var journalMode = await GetScalarStringAsync(
            connection,
            "PRAGMA journal_mode;",
            TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedTables, tableNames.Order(StringComparer.Ordinal));
        Assert.Equal("wal", journalMode, ignoreCase: true);
    }

    [Fact]
    public async Task InitializeAsyncIsIdempotent()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var firstInitializer = CreateInitializer(paths);
        using var secondInitializer = CreateInitializer(paths);

        await firstInitializer.InitializeAsync(TestContext.Current.CancellationToken);
        await secondInitializer.InitializeAsync(TestContext.Current.CancellationToken);

        await using var connection = CreateConnection(paths);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var migrationCount = await GetScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM SchemaMigrations;",
            TestContext.Current.CancellationToken);

        Assert.Equal(4, migrationCount);
    }

    [Fact]
    public async Task InitializeAsyncHonorsPreCancelledToken()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = CreateInitializer(paths);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => initializer.InitializeAsync(cancellation.Token));
    }

    private static SqliteDatabaseInitializer CreateInitializer(GameNestDataPaths paths) =>
        new(paths, NullLogger<SqliteDatabaseInitializer>.Instance);

    internal static SqliteConnection CreateConnection(GameNestDataPaths paths) =>
        new(
            new SqliteConnectionStringBuilder
            {
                DataSource = paths.DatabaseFile,
                ForeignKeys = true,
            }.ToString());

    private static async Task<string[]> GetTableNamesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var names = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }

    private static async Task<string> GetScalarStringAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Assert.IsType<string>(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long> GetScalarInt64Async(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Assert.IsType<long>(await command.ExecuteScalarAsync(cancellationToken));
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    private TemporaryDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TemporaryDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "GameNest.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TemporaryDirectory(path);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        const int maximumAttempts = 40;
        for (var attempt = 1; attempt <= maximumAttempts && Directory.Exists(Path); attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < maximumAttempts)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < maximumAttempts)
            {
                Thread.Sleep(50);
            }
        }
    }
}
