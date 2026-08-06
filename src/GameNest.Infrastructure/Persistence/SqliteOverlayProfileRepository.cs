using System.Globalization;
using GameNest.Application;
using GameNest.Domain;

namespace GameNest.Infrastructure.Persistence;

public sealed class SqliteOverlayProfileRepository(
    GameNestDataPaths paths,
    IApplicationDataInitializer initializer) : IOverlayProfileRepository
{
    public async Task<OverlayProfile> GetGlobalAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var createCommand = connection.CreateCommand())
        {
            var profile = OverlayProfile.CreateDefault();
            createCommand.CommandText =
                """
                INSERT INTO OverlayProfiles (
                    Id, GameId, IsEnabled, Position, ScalePercent,
                    BackgroundOpacityPercent, ShowFps, ShowCpu, ShowGpu, ShowRam,
                    ToggleHotkey, HideWhenGameNotForeground, UpdatedAtUtc)
                SELECT
                    $id, NULL, $isEnabled, $position, $scalePercent,
                    $backgroundOpacityPercent, $showFps, $showCpu, $showGpu, $showRam,
                    $toggleHotkey, $hideWhenGameNotForeground, $updatedAtUtc
                WHERE NOT EXISTS (SELECT 1 FROM OverlayProfiles WHERE GameId IS NULL);
                """;
            AddParameters(createCommand, profile);
            await createCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return await ReadAsync(connection, null, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("无法创建全局覆盖层配置。");
    }

    public async Task<OverlayProfile?> GetForGameAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        if (gameId == Guid.Empty)
        {
            throw new ArgumentException("游戏 ID 不能为空。", nameof(gameId));
        }

        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, gameId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(OverlayProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        updateCommand.CommandText =
            """
            UPDATE OverlayProfiles SET
                IsEnabled = $isEnabled,
                Position = $position,
                ScalePercent = $scalePercent,
                BackgroundOpacityPercent = $backgroundOpacityPercent,
                ShowFps = $showFps,
                ShowCpu = $showCpu,
                ShowGpu = $showGpu,
                ShowRam = $showRam,
                ToggleHotkey = $toggleHotkey,
                HideWhenGameNotForeground = $hideWhenGameNotForeground,
                UpdatedAtUtc = $updatedAtUtc
            WHERE ($gameId IS NULL AND GameId IS NULL) OR GameId = $gameId;
            """;
        AddParameters(updateCommand, profile);
        var updated = await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (updated == 0)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            insertCommand.CommandText =
                """
                INSERT INTO OverlayProfiles (
                    Id, GameId, IsEnabled, Position, ScalePercent,
                    BackgroundOpacityPercent, ShowFps, ShowCpu, ShowGpu, ShowRam,
                    ToggleHotkey, HideWhenGameNotForeground, UpdatedAtUtc)
                VALUES (
                    $id, $gameId, $isEnabled, $position, $scalePercent,
                    $backgroundOpacityPercent, $showFps, $showCpu, $showGpu, $showRam,
                    $toggleHotkey, $hideWhenGameNotForeground, $updatedAtUtc);
                """;
            AddParameters(insertCommand, profile);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveForGameAsync(Guid gameId, CancellationToken cancellationToken)
    {
        if (gameId == Guid.Empty)
        {
            throw new ArgumentException("游戏 ID 不能为空。", nameof(gameId));
        }

        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM OverlayProfiles WHERE GameId = $gameId;";
        command.Parameters.AddWithValue("$gameId", gameId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<OverlayProfile?> ReadAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Guid? gameId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                Id, GameId, IsEnabled, Position, ScalePercent,
                BackgroundOpacityPercent, ShowFps, ShowCpu, ShowGpu, ShowRam,
                ToggleHotkey, HideWhenGameNotForeground, UpdatedAtUtc
            FROM OverlayProfiles
            WHERE ($gameId IS NULL AND GameId IS NULL) OR GameId = $gameId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$gameId", gameId is null ? DBNull.Value : gameId.Value.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new OverlayProfile(
            Guid.Parse(reader.GetString(0)),
            reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            reader.GetInt32(2) != 0,
            Enum.Parse<OverlayPosition>(reader.GetString(3), ignoreCase: true),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6) != 0,
            reader.GetInt32(7) != 0,
            reader.GetInt32(8) != 0,
            reader.GetInt32(9) != 0,
            reader.GetString(10),
            reader.GetInt32(11) != 0,
            DateTimeOffset.Parse(
                reader.GetString(12),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));
    }

    private static void AddParameters(
        Microsoft.Data.Sqlite.SqliteCommand command,
        OverlayProfile profile)
    {
        command.Parameters.AddWithValue("$id", profile.Id.ToString("D"));
        command.Parameters.AddWithValue(
            "$gameId",
            profile.GameId is null ? DBNull.Value : profile.GameId.Value.ToString("D"));
        command.Parameters.AddWithValue("$isEnabled", profile.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$position", profile.Position.ToString());
        command.Parameters.AddWithValue("$scalePercent", profile.ScalePercent);
        command.Parameters.AddWithValue("$backgroundOpacityPercent", profile.BackgroundOpacityPercent);
        command.Parameters.AddWithValue("$showFps", profile.ShowFps ? 1 : 0);
        command.Parameters.AddWithValue("$showCpu", profile.ShowCpu ? 1 : 0);
        command.Parameters.AddWithValue("$showGpu", profile.ShowGpu ? 1 : 0);
        command.Parameters.AddWithValue("$showRam", profile.ShowRam ? 1 : 0);
        command.Parameters.AddWithValue("$toggleHotkey", profile.ToggleHotkey);
        command.Parameters.AddWithValue(
            "$hideWhenGameNotForeground",
            profile.HideWhenGameNotForeground ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAtUtc", profile.UpdatedAtUtc.ToString("O"));
    }
}
