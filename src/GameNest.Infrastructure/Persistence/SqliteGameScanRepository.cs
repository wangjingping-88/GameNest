using System.Globalization;
using System.Text.Json;
using GameNest.Application;
using GameNest.Domain;
using Microsoft.Data.Sqlite;

namespace GameNest.Infrastructure.Persistence;

public sealed class SqliteGameScanRepository(
    GameNestDataPaths paths,
    IApplicationDataInitializer initializer) : IGameScanRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ScanRoot>> GetRootsAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, VolumeIdentity, CurrentPath, RelativePath, ScanMode,
                   IsEnabled, IsOnline, LastScanUtc, LastCheckpoint
            FROM ScanRoots
            ORDER BY CurrentPath COLLATE NOCASE;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var roots = new List<ScanRoot>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            roots.Add(ReadRoot(reader));
        }

        return roots;
    }

    public async Task AddRootAsync(ScanRoot root, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ScanRoots (
                Id, VolumeIdentity, CurrentPath, RelativePath, ScanMode, IsEnabled,
                IsOnline, LastScanUtc, LastCheckpoint)
            VALUES (
                $id, $volumeIdentity, $currentPath, $relativePath, $scanMode, $isEnabled,
                $isOnline, $lastScanUtc, $lastCheckpoint);
            """;
        AddRootParameters(command, root);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateRootAsync(ScanRoot root, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE ScanRoots SET
                VolumeIdentity = $volumeIdentity,
                CurrentPath = $currentPath,
                RelativePath = $relativePath,
                ScanMode = $scanMode,
                IsEnabled = $isEnabled,
                IsOnline = $isOnline,
                LastScanUtc = $lastScanUtc,
                LastCheckpoint = $lastCheckpoint
            WHERE Id = $id;
            """;
        AddRootParameters(command, root);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new KeyNotFoundException("找不到要更新的扫描根目录。");
        }
    }

    public async Task<bool> RemoveRootAsync(Guid rootId, CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ScanRoots WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<IReadOnlyList<GameCandidate>> GetCandidatesAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCandidateSelectCommand(connection, predicate: null);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var candidates = new List<GameCandidate>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            candidates.Add(ReadCandidate(reader));
        }

        return candidates;
    }

    public async Task<GameCandidate?> GetCandidateAsync(
        Guid candidateId,
        CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCandidateSelectCommand(connection, "Id = $id");
        command.Parameters.AddWithValue("$id", candidateId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadCandidate(reader) : null;
    }

    public async Task<Guid> StartRunAsync(ScanMode mode, CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var runId = Guid.NewGuid();
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ScanRuns (Id, ScanMode, Status, StartedAtUtc)
            VALUES ($id, $scanMode, $status, $startedAtUtc);
            """;
        command.Parameters.AddWithValue("$id", runId.ToString("D"));
        command.Parameters.AddWithValue("$scanMode", mode.ToString());
        command.Parameters.AddWithValue("$status", GameScanRunStatus.Running.ToString());
        command.Parameters.AddWithValue("$startedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return runId;
    }

    public async Task SaveCandidatesAsync(
        Guid runId,
        IReadOnlyList<GameCandidate> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = (SqliteTransaction)transaction;
            deleteCommand.CommandText = "DELETE FROM ScanCandidates WHERE Decision = 'Pending';";
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var candidate in candidates)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText =
                """
                INSERT INTO ScanCandidates (
                    Id, LastSeenRunId, ScanRootId, AdapterId, Source, SourceGameId,
                    Title, ExecutablePath, Arguments, WorkingDirectory, InstallRoot,
                    VolumeIdentity, Fingerprint, Score, EvidenceJson, GroupKey,
                    IsPrimary, Decision, DiscoveredAtUtc)
                VALUES (
                    $id, $runId, $scanRootId, $adapterId, $source, $sourceGameId,
                    $title, $executablePath, $arguments, $workingDirectory, $installRoot,
                    $volumeIdentity, $fingerprint, $score, $evidenceJson, $groupKey,
                    $isPrimary, $decision, $discoveredAtUtc)
                ON CONFLICT(Id) DO UPDATE SET
                    LastSeenRunId = excluded.LastSeenRunId,
                    Score = excluded.Score,
                    EvidenceJson = excluded.EvidenceJson,
                    GroupKey = excluded.GroupKey,
                    IsPrimary = excluded.IsPrimary,
                    Decision = excluded.Decision,
                    DiscoveredAtUtc = excluded.DiscoveredAtUtc;
                """;
            AddCandidateParameters(command, runId, candidate);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteRunAsync(
        Guid runId,
        GameScanRunStatus status,
        long checkedDirectoryCount,
        int candidateCount,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE ScanRuns SET
                Status = $status,
                FinishedAtUtc = $finishedAtUtc,
                CheckedDirectoryCount = $checkedDirectoryCount,
                CandidateCount = $candidateCount,
                ErrorMessage = $errorMessage
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", runId.ToString("D"));
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$finishedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$checkedDirectoryCount", checkedDirectoryCount);
        command.Parameters.AddWithValue("$candidateCount", candidateCount);
        command.Parameters.AddWithValue("$errorMessage", (object?)errorMessage ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetExcludedDirectoriesAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DirectoryPath FROM ScanExclusions ORDER BY CreatedAtUtc DESC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var excludedPaths = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            excludedPaths.Add(reader.GetString(0));
        }

        return excludedPaths;
    }

    public async Task AddExcludedDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ScanExclusions (Id, DirectoryPath, CreatedAtUtc)
            VALUES ($id, $path, $createdAtUtc)
            ON CONFLICT(DirectoryPath) DO UPDATE SET CreatedAtUtc = excluded.CreatedAtUtc;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$path", Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
        command.Parameters.AddWithValue("$createdAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> UndoLastExcludedDirectoryAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string? path = null;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = (SqliteTransaction)transaction;
            select.CommandText =
                "SELECT DirectoryPath FROM ScanExclusions ORDER BY CreatedAtUtc DESC LIMIT 1;";
            path = (string?)await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }

        if (path is not null)
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM ScanExclusions WHERE DirectoryPath = $path COLLATE NOCASE;";
            delete.Parameters.AddWithValue("$path", path);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return path;
    }

    public async Task SetCandidateDecisionAsync(
        Guid candidateId,
        GameCandidateDecision decision,
        CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ScanCandidates SET Decision = $decision WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", candidateId.ToString("D"));
        command.Parameters.AddWithValue("$decision", decision.ToString());
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new KeyNotFoundException("找不到要更新的扫描候选。");
        }
    }

    private static SqliteCommand CreateCandidateSelectCommand(SqliteConnection connection, string? predicate)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            $$"""
            SELECT Id, ScanRootId, AdapterId, Source, SourceGameId, Title,
                   ExecutablePath, Arguments, WorkingDirectory, InstallRoot,
                   VolumeIdentity, Fingerprint, Score, EvidenceJson, GroupKey,
                   IsPrimary, Decision, DiscoveredAtUtc
            FROM ScanCandidates
            {{(predicate is null ? string.Empty : $"WHERE {predicate}")}}
            ORDER BY Score DESC, Title COLLATE NOCASE;
            """;
        return command;
    }

    private static ScanRoot ReadRoot(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            Enum.Parse<ScanMode>(reader.GetString(4)),
            reader.GetBoolean(5),
            reader.GetBoolean(6),
            reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7)),
            reader.IsDBNull(8) ? null : reader.GetString(8));

    private static GameCandidate ReadCandidate(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            Enum.Parse<GameCandidateSource>(reader.GetString(3)),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetString(11),
            reader.GetInt32(12),
            JsonSerializer.Deserialize<GameCandidateEvidence[]>(reader.GetString(13), JsonOptions) ?? [],
            reader.GetString(14),
            reader.GetBoolean(15),
            Enum.Parse<GameCandidateDecision>(reader.GetString(16)),
            ParseDate(reader.GetString(17)));

    private static void AddRootParameters(SqliteCommand command, ScanRoot root)
    {
        command.Parameters.AddWithValue("$id", root.Id.ToString("D"));
        command.Parameters.AddWithValue("$volumeIdentity", root.VolumeIdentity);
        command.Parameters.AddWithValue("$currentPath", root.CurrentPath);
        command.Parameters.AddWithValue("$relativePath", root.RelativePath);
        command.Parameters.AddWithValue("$scanMode", root.ScanMode.ToString());
        command.Parameters.AddWithValue("$isEnabled", root.IsEnabled);
        command.Parameters.AddWithValue("$isOnline", root.IsOnline);
        command.Parameters.AddWithValue("$lastScanUtc", root.LastScanUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$lastCheckpoint", (object?)root.LastCheckpoint ?? DBNull.Value);
    }

    private static void AddCandidateParameters(
        SqliteCommand command,
        Guid runId,
        GameCandidate candidate)
    {
        command.Parameters.AddWithValue("$id", candidate.Id.ToString("D"));
        command.Parameters.AddWithValue("$runId", runId.ToString("D"));
        command.Parameters.AddWithValue("$scanRootId", candidate.ScanRootId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$adapterId", candidate.AdapterId);
        command.Parameters.AddWithValue("$source", candidate.Source.ToString());
        command.Parameters.AddWithValue("$sourceGameId", (object?)candidate.SourceGameId ?? DBNull.Value);
        command.Parameters.AddWithValue("$title", candidate.Title);
        command.Parameters.AddWithValue("$executablePath", candidate.ExecutablePath);
        command.Parameters.AddWithValue("$arguments", (object?)candidate.Arguments ?? DBNull.Value);
        command.Parameters.AddWithValue("$workingDirectory", candidate.WorkingDirectory);
        command.Parameters.AddWithValue("$installRoot", candidate.InstallRoot);
        command.Parameters.AddWithValue("$volumeIdentity", (object?)candidate.VolumeIdentity ?? DBNull.Value);
        command.Parameters.AddWithValue("$fingerprint", candidate.Fingerprint);
        command.Parameters.AddWithValue("$score", candidate.Score);
        command.Parameters.AddWithValue("$evidenceJson", JsonSerializer.Serialize(candidate.Evidence, JsonOptions));
        command.Parameters.AddWithValue("$groupKey", candidate.GroupKey);
        command.Parameters.AddWithValue("$isPrimary", candidate.IsPrimary);
        command.Parameters.AddWithValue("$decision", candidate.Decision.ToString());
        command.Parameters.AddWithValue("$discoveredAtUtc", candidate.DiscoveredAtUtc.ToString("O"));
    }

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
