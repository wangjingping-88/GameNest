using System.Globalization;
using System.Text.Json;
using GameNest.Application;
using GameNest.Domain;
using Microsoft.Data.Sqlite;

namespace GameNest.Infrastructure.Persistence;

public sealed class SqliteGameRuntimeRepository(
    GameNestDataPaths paths,
    IApplicationDataInitializer initializer) : IGameRuntimeRepository
{
    public async Task StartSessionAsync(PlaySession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsActive)
        {
            throw new ArgumentException("只能创建尚未结束的会话。", nameof(session));
        }

        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO PlaySessions (
                Id, GameId, StartedAtUtc, EndedAtUtc, DurationSeconds,
                ExitKind, TrackedProcessIds)
            VALUES ($id, $gameId, $startedAtUtc, NULL, NULL, NULL, $trackedProcessIds);
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$gameId", session.GameId.ToString("D"));
        command.Parameters.AddWithValue("$startedAtUtc", session.StartedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$trackedProcessIds", JsonSerializer.Serialize(session.TrackedProcessIds));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateTrackedProcessIdsAsync(
        Guid sessionId,
        IReadOnlyCollection<int> processIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE PlaySessions SET TrackedProcessIds = $processIds WHERE Id = $id AND EndedAtUtc IS NULL;";
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        command.Parameters.AddWithValue(
            "$processIds",
            JsonSerializer.Serialize(processIds.Distinct().Order().ToArray()));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlaySession?> CompleteSessionAsync(
        Guid sessionId,
        DateTimeOffset endedAtUtc,
        GameExitKind exitKind,
        CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var active = await ReadByIdAsync(
                connection,
                (SqliteTransaction)transaction,
                sessionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (active is null || !active.IsActive)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return active;
        }

        var completed = active.Complete(endedAtUtc, exitKind);
        await using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.Transaction = (SqliteTransaction)transaction;
            sessionCommand.CommandText =
                """
                UPDATE PlaySessions SET
                    EndedAtUtc = $endedAtUtc,
                    DurationSeconds = $durationSeconds,
                    ExitKind = $exitKind,
                    TrackedProcessIds = $trackedProcessIds
                WHERE Id = $id AND EndedAtUtc IS NULL;
                """;
            sessionCommand.Parameters.AddWithValue("$id", completed.Id.ToString("D"));
            sessionCommand.Parameters.AddWithValue("$endedAtUtc", completed.EndedAtUtc!.Value.ToString("O"));
            sessionCommand.Parameters.AddWithValue("$durationSeconds", completed.DurationSeconds!.Value);
            sessionCommand.Parameters.AddWithValue("$exitKind", completed.ExitKind!.Value.ToString());
            sessionCommand.Parameters.AddWithValue(
                "$trackedProcessIds",
                JsonSerializer.Serialize(completed.TrackedProcessIds));
            if (await sessionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }
        }

        await using (var gameCommand = connection.CreateCommand())
        {
            gameCommand.Transaction = (SqliteTransaction)transaction;
            gameCommand.CommandText =
                """
                UPDATE Games SET
                    LastPlayedUtc = $startedAtUtc,
                    TotalPlaySeconds = TotalPlaySeconds + $durationSeconds
                WHERE Id = $gameId;
                """;
            gameCommand.Parameters.AddWithValue("$gameId", completed.GameId.ToString("D"));
            gameCommand.Parameters.AddWithValue("$startedAtUtc", completed.StartedAtUtc.ToString("O"));
            gameCommand.Parameters.AddWithValue("$durationSeconds", completed.DurationSeconds.Value);
            await gameCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return completed;
    }

    public async Task<IReadOnlyList<PlaySession>> GetSessionsAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, GameId, StartedAtUtc, EndedAtUtc, DurationSeconds,
                   ExitKind, TrackedProcessIds
            FROM PlaySessions
            WHERE GameId = $gameId
            ORDER BY StartedAtUtc DESC;
            """;
        command.Parameters.AddWithValue("$gameId", gameId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var sessions = new List<PlaySession>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sessions.Add(ReadSession(reader));
        }

        return sessions;
    }

    public async Task<IReadOnlyList<PlaySession>> GetActiveSessionsAsync(
        CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, GameId, StartedAtUtc, EndedAtUtc, DurationSeconds,
                   ExitKind, TrackedProcessIds
            FROM PlaySessions
            WHERE EndedAtUtc IS NULL
            ORDER BY StartedAtUtc;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var sessions = new List<PlaySession>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sessions.Add(ReadSession(reader));
        }

        return sessions;
    }

    private static async Task<PlaySession?> ReadByIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT Id, GameId, StartedAtUtc, EndedAtUtc, DurationSeconds,
                   ExitKind, TrackedProcessIds
            FROM PlaySessions
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadSession(reader)
            : null;
    }

    private static PlaySession ReadSession(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            DateTimeOffset.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(3)
                ? null
                : DateTimeOffset.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : Enum.Parse<GameExitKind>(reader.GetString(5)),
            JsonSerializer.Deserialize<int[]>(reader.GetString(6)) ?? []);
}
