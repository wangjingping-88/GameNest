using GameNest.Application;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GameNest.Infrastructure.Persistence;

public sealed class SqliteDatabaseInitializer(
    GameNestDataPaths paths,
    ILogger<SqliteDatabaseInitializer> logger) : IApplicationDataInitializer, IDisposable
{
    private static readonly Action<ILogger, int, Exception?> DatabaseInitialized =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1000, nameof(DatabaseInitialized)),
            "本地数据库初始化完成，共应用 {MigrationCount} 个迁移。");

    private static readonly Action<ILogger, Exception?> DatabaseInitializationFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1001, nameof(DatabaseInitializationFailed)),
            "本地数据库初始化失败。");

    private static readonly IReadOnlyList<DatabaseMigration> Migrations =
    [
        new DatabaseMigration("001_initial", InitialSchemaSql),
        new DatabaseMigration("002_phase2_scan", PhaseTwoScanSchemaSql),
        new DatabaseMigration("003_phase3_runtime", PhaseThreeRuntimeSchemaSql),
        new DatabaseMigration("004_phase5_metadata", PhaseFiveMetadataSchemaSql),
    ];

    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            var databaseDirectory = Path.GetDirectoryName(paths.DatabaseFile)
                ?? throw new InvalidOperationException("无法确定数据库目录。");

            await Task.Run(
                    () => Directory.CreateDirectory(databaseDirectory),
                    cancellationToken)
                .ConfigureAwait(false);

            await using var connection = SqliteConnectionFactory.Create(paths);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            await EnsureMigrationTableAsync(connection, cancellationToken).ConfigureAwait(false);

            var appliedMigrations = await GetAppliedMigrationsAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            foreach (var migration in Migrations)
            {
                if (!appliedMigrations.Contains(migration.Id))
                {
                    await ApplyMigrationAsync(connection, migration, cancellationToken).ConfigureAwait(false);
                }
            }

            _initialized = true;
            DatabaseInitialized(logger, Migrations.Count, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DatabaseInitializationFailed(logger, exception);
            throw;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _initializationGate.Dispose();
        _disposed = true;
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 15000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureMigrationTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Id TEXT NOT NULL PRIMARY KEY,
                AppliedAtUtc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> GetAppliedMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var appliedMigrations = new HashSet<string>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM SchemaMigrations;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            appliedMigrations.Add(reader.GetString(0));
        }

        return appliedMigrations;
    }

    private static async Task ApplyMigrationAsync(
        SqliteConnection connection,
        DatabaseMigration migration,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var migrationCommand = connection.CreateCommand())
        {
            migrationCommand.Transaction = (SqliteTransaction)transaction;
            migrationCommand.CommandText = migration.Sql;
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var recordCommand = connection.CreateCommand())
        {
            recordCommand.Transaction = (SqliteTransaction)transaction;
            recordCommand.CommandText =
                "INSERT INTO SchemaMigrations (Id, AppliedAtUtc) VALUES ($id, $appliedAtUtc);";
            recordCommand.Parameters.AddWithValue("$id", migration.Id);
            recordCommand.Parameters.AddWithValue("$appliedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            await recordCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record DatabaseMigration(string Id, string Sql);

    private const string InitialSchemaSql =
        """
        CREATE TABLE Games (
            Id TEXT NOT NULL PRIMARY KEY,
            Title TEXT NOT NULL,
            SortTitle TEXT NOT NULL,
            Description TEXT NULL,
            InstallRoot TEXT NOT NULL,
            SourceType TEXT NOT NULL,
            SourceGameId TEXT NULL,
            VolumeIdentity TEXT NULL,
            IsFavorite INTEGER NOT NULL DEFAULT 0 CHECK (IsFavorite IN (0, 1)),
            IsHidden INTEGER NOT NULL DEFAULT 0 CHECK (IsHidden IN (0, 1)),
            Availability TEXT NOT NULL,
            DetectionConfidence INTEGER NOT NULL DEFAULT 0,
            UserEditedFields TEXT NOT NULL DEFAULT '[]',
            DateAddedUtc TEXT NOT NULL,
            LastPlayedUtc TEXT NULL,
            TotalPlaySeconds INTEGER NOT NULL DEFAULT 0 CHECK (TotalPlaySeconds >= 0)
        );

        CREATE TABLE LaunchProfiles (
            Id TEXT NOT NULL PRIMARY KEY,
            GameId TEXT NOT NULL,
            Name TEXT NOT NULL,
            LaunchKind TEXT NOT NULL,
            ExecutablePath TEXT NULL,
            Arguments TEXT NULL,
            WorkingDirectory TEXT NULL,
            RunAsAdministrator INTEGER NOT NULL DEFAULT 0 CHECK (RunAsAdministrator IN (0, 1)),
            ExpectedProcessNames TEXT NOT NULL DEFAULT '[]',
            IsDefault INTEGER NOT NULL DEFAULT 0 CHECK (IsDefault IN (0, 1)),
            GracefulStopTimeoutSeconds INTEGER NOT NULL DEFAULT 10 CHECK (GracefulStopTimeoutSeconds > 0),
            FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
        );

        CREATE TABLE GameAssets (
            Id TEXT NOT NULL PRIMARY KEY,
            GameId TEXT NOT NULL,
            AssetType TEXT NOT NULL,
            LocalPath TEXT NOT NULL,
            Source TEXT NOT NULL,
            Width INTEGER NOT NULL CHECK (Width >= 0),
            Height INTEGER NOT NULL CHECK (Height >= 0),
            ContentHash TEXT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
        );

        CREATE TABLE ScanRoots (
            Id TEXT NOT NULL PRIMARY KEY,
            VolumeIdentity TEXT NOT NULL,
            CurrentPath TEXT NOT NULL,
            RelativePath TEXT NOT NULL,
            ScanMode TEXT NOT NULL,
            IsEnabled INTEGER NOT NULL DEFAULT 1 CHECK (IsEnabled IN (0, 1)),
            LastScanUtc TEXT NULL,
            LastCheckpoint TEXT NULL
        );

        CREATE TABLE PlaySessions (
            Id TEXT NOT NULL PRIMARY KEY,
            GameId TEXT NOT NULL,
            StartedAtUtc TEXT NOT NULL,
            EndedAtUtc TEXT NULL,
            DurationSeconds INTEGER NULL CHECK (DurationSeconds IS NULL OR DurationSeconds >= 0),
            ExitKind TEXT NULL,
            TrackedProcessIds TEXT NOT NULL DEFAULT '[]',
            FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
        );

        CREATE TABLE OverlayProfiles (
            Id TEXT NOT NULL PRIMARY KEY,
            GameId TEXT NULL,
            IsEnabled INTEGER NOT NULL DEFAULT 1 CHECK (IsEnabled IN (0, 1)),
            Position TEXT NOT NULL DEFAULT 'TopRight',
            ScalePercent INTEGER NOT NULL DEFAULT 100,
            BackgroundOpacityPercent INTEGER NOT NULL DEFAULT 88,
            ShowFps INTEGER NOT NULL DEFAULT 1 CHECK (ShowFps IN (0, 1)),
            ShowCpu INTEGER NOT NULL DEFAULT 1 CHECK (ShowCpu IN (0, 1)),
            ShowGpu INTEGER NOT NULL DEFAULT 1 CHECK (ShowGpu IN (0, 1)),
            ShowRam INTEGER NOT NULL DEFAULT 1 CHECK (ShowRam IN (0, 1)),
            ToggleHotkey TEXT NOT NULL DEFAULT 'Ctrl+Shift+F12',
            HideWhenGameNotForeground INTEGER NOT NULL DEFAULT 1 CHECK (HideWhenGameNotForeground IN (0, 1)),
            UpdatedAtUtc TEXT NOT NULL,
            FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
        );

        CREATE TABLE AppSettings (
            Key TEXT NOT NULL PRIMARY KEY,
            Value TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL
        );

        CREATE INDEX IX_Games_SortTitle ON Games (SortTitle);
        CREATE INDEX IX_LaunchProfiles_GameId ON LaunchProfiles (GameId);
        CREATE INDEX IX_GameAssets_GameId ON GameAssets (GameId);
        CREATE INDEX IX_PlaySessions_GameId_StartedAtUtc ON PlaySessions (GameId, StartedAtUtc DESC);
        CREATE UNIQUE INDEX UX_OverlayProfiles_GameId ON OverlayProfiles (GameId) WHERE GameId IS NOT NULL;
        CREATE UNIQUE INDEX UX_OverlayProfiles_Global ON OverlayProfiles ((1)) WHERE GameId IS NULL;
        """;

    private const string PhaseTwoScanSchemaSql =
        """
        ALTER TABLE ScanRoots ADD COLUMN IsOnline INTEGER NOT NULL DEFAULT 1 CHECK (IsOnline IN (0, 1));

        CREATE TABLE ScanRuns (
            Id TEXT NOT NULL PRIMARY KEY,
            ScanMode TEXT NOT NULL,
            Status TEXT NOT NULL,
            StartedAtUtc TEXT NOT NULL,
            FinishedAtUtc TEXT NULL,
            CheckedDirectoryCount INTEGER NOT NULL DEFAULT 0 CHECK (CheckedDirectoryCount >= 0),
            CandidateCount INTEGER NOT NULL DEFAULT 0 CHECK (CandidateCount >= 0),
            ErrorMessage TEXT NULL
        );

        CREATE TABLE ScanCandidates (
            Id TEXT NOT NULL PRIMARY KEY,
            LastSeenRunId TEXT NOT NULL,
            ScanRootId TEXT NULL,
            AdapterId TEXT NOT NULL,
            Source TEXT NOT NULL,
            SourceGameId TEXT NULL,
            Title TEXT NOT NULL,
            ExecutablePath TEXT NOT NULL,
            Arguments TEXT NULL,
            WorkingDirectory TEXT NOT NULL,
            InstallRoot TEXT NOT NULL,
            VolumeIdentity TEXT NULL,
            Fingerprint TEXT NOT NULL,
            Score INTEGER NOT NULL,
            EvidenceJson TEXT NOT NULL,
            GroupKey TEXT NOT NULL,
            IsPrimary INTEGER NOT NULL DEFAULT 1 CHECK (IsPrimary IN (0, 1)),
            Decision TEXT NOT NULL,
            DiscoveredAtUtc TEXT NOT NULL,
            FOREIGN KEY (LastSeenRunId) REFERENCES ScanRuns (Id) ON DELETE CASCADE,
            FOREIGN KEY (ScanRootId) REFERENCES ScanRoots (Id) ON DELETE CASCADE
        );

        CREATE TABLE ScanExclusions (
            Id TEXT NOT NULL PRIMARY KEY,
            DirectoryPath TEXT NOT NULL COLLATE NOCASE,
            CreatedAtUtc TEXT NOT NULL
        );

        CREATE UNIQUE INDEX UX_ScanRoots_CurrentPath ON ScanRoots (CurrentPath COLLATE NOCASE);
        CREATE INDEX IX_ScanCandidates_GroupKey ON ScanCandidates (GroupKey);
        CREATE INDEX IX_ScanCandidates_ExecutablePath ON ScanCandidates (ExecutablePath COLLATE NOCASE);
        CREATE INDEX IX_ScanCandidates_Decision_Score ON ScanCandidates (Decision, Score DESC);
        CREATE UNIQUE INDEX UX_ScanExclusions_DirectoryPath ON ScanExclusions (DirectoryPath COLLATE NOCASE);
        """;

    private const string PhaseThreeRuntimeSchemaSql =
        """
        CREATE UNIQUE INDEX UX_PlaySessions_ActiveGame
            ON PlaySessions (GameId)
            WHERE EndedAtUtc IS NULL;
        CREATE INDEX IX_PlaySessions_GameId_EndedAtUtc
            ON PlaySessions (GameId, EndedAtUtc DESC);
        """;

    private const string PhaseFiveMetadataSchemaSql =
        """
        ALTER TABLE Games ADD COLUMN MetadataProviderId TEXT NULL;
        ALTER TABLE Games ADD COLUMN MetadataSourceId TEXT NULL;
        ALTER TABLE Games ADD COLUMN MetadataSourceName TEXT NULL;
        ALTER TABLE Games ADD COLUMN MetadataUpdatedAtUtc TEXT NULL;

        CREATE TABLE MetadataRevisions (
            Id TEXT NOT NULL PRIMARY KEY,
            GameId TEXT NOT NULL,
            PreviousTitle TEXT NOT NULL,
            PreviousDescription TEXT NULL,
            PreviousProviderId TEXT NULL,
            PreviousSourceId TEXT NULL,
            PreviousSourceName TEXT NULL,
            PreviousUpdatedAtUtc TEXT NULL,
            AppliedProviderId TEXT NOT NULL,
            AppliedSourceId TEXT NOT NULL,
            AppliedAtUtc TEXT NOT NULL,
            UndoneAtUtc TEXT NULL,
            FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
        );

        CREATE INDEX IX_MetadataRevisions_GameId_AppliedAtUtc
            ON MetadataRevisions (GameId, AppliedAtUtc DESC);
        """;
}
