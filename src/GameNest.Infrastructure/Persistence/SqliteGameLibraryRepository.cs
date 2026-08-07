using GameNest.Application;
using GameNest.Domain;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace GameNest.Infrastructure.Persistence;

public sealed class SqliteGameLibraryRepository(
    GameNestDataPaths paths,
    IApplicationDataInitializer initializer) : IGameLibraryRepository
{
    public async Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateSelectCommand(connection, null);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var games = new List<Game>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            games.Add(ReadGame(reader));
        }

        return games;
    }

    public async Task<Game?> GetByIdAsync(Guid gameId, CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateSelectCommand(connection, "g.Id = $gameId");
        command.Parameters.AddWithValue("$gameId", gameId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadGame(reader) : null;
    }

    public async Task<Game?> FindByExecutablePathAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateSelectCommand(
            connection,
            "lp.ExecutablePath = $executablePath COLLATE NOCASE");
        command.Parameters.AddWithValue("$executablePath", executablePath);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadGame(reader) : null;
    }

    public async Task AddAsync(Game game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await InsertGameAsync(connection, (SqliteTransaction)transaction, game, cancellationToken)
            .ConfigureAwait(false);
        await InsertLaunchProfileAsync(connection, (SqliteTransaction)transaction, game.LaunchProfile, cancellationToken)
            .ConfigureAwait(false);
        if (game.Icon is not null)
        {
            await UpsertAssetAsync(connection, (SqliteTransaction)transaction, game.Icon, cancellationToken)
                .ConfigureAwait(false);
        }
        if (game.Cover is not null)
        {
            await UpsertAssetAsync(connection, (SqliteTransaction)transaction, game.Cover, cancellationToken)
                .ConfigureAwait(false);
        }
        if (game.Hero is not null)
        {
            await UpsertAssetAsync(connection, (SqliteTransaction)transaction, game.Hero, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Game game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var gameCommand = connection.CreateCommand())
        {
            gameCommand.Transaction = (SqliteTransaction)transaction;
            gameCommand.CommandText =
                """
                UPDATE Games SET
                    Title = $title,
                    SortTitle = $sortTitle,
                    Description = $description,
                    InstallRoot = $installRoot,
                    SourceGameId = $sourceGameId,
                    VolumeIdentity = $volumeIdentity,
                    IsFavorite = $isFavorite,
                    Availability = $availability,
                    DetectionConfidence = $detectionConfidence,
                    UserEditedFields = $userEditedFields,
                    MetadataProviderId = $metadataProviderId,
                    MetadataSourceId = $metadataSourceId,
                    MetadataSourceName = $metadataSourceName,
                    MetadataUpdatedAtUtc = $metadataUpdatedAtUtc,
                    LastPlayedUtc = $lastPlayedUtc,
                    TotalPlaySeconds = $totalPlaySeconds
                WHERE Id = $id;
                """;
            AddGameParameters(gameCommand, game);
            var affected = await gameCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected != 1)
            {
                throw new KeyNotFoundException("找不到要更新的游戏。");
            }
        }

        await using (var profileCommand = connection.CreateCommand())
        {
            profileCommand.Transaction = (SqliteTransaction)transaction;
            profileCommand.CommandText =
                """
                UPDATE LaunchProfiles SET
                    Name = $name,
                    LaunchKind = $launchKind,
                    ExecutablePath = $executablePath,
                    Arguments = $arguments,
                    WorkingDirectory = $workingDirectory,
                    RunAsAdministrator = $runAsAdministrator,
                    ExpectedProcessNames = $expectedProcessNames,
                    GracefulStopTimeoutSeconds = $gracefulStopTimeoutSeconds,
                    IsDefault = $isDefault
                WHERE Id = $id AND GameId = $gameId;
                """;
            AddLaunchProfileParameters(profileCommand, game.LaunchProfile);
            var affected = await profileCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected != 1)
            {
                throw new KeyNotFoundException("找不到要更新的启动配置。");
            }
        }

        if (game.Icon is not null)
        {
            await UpsertAssetAsync(connection, (SqliteTransaction)transaction, game.Icon, cancellationToken)
                .ConfigureAwait(false);
        }
        if (game.Cover is not null)
        {
            await UpsertAssetAsync(connection, (SqliteTransaction)transaction, game.Cover, cancellationToken)
                .ConfigureAwait(false);
        }
        if (game.Hero is not null)
        {
            await UpsertAssetAsync(connection, (SqliteTransaction)transaction, game.Hero, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(Guid gameId, CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Games WHERE Id = $gameId;";
        command.Parameters.AddWithValue("$gameId", gameId.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task SetIconAsync(GameAsset icon, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(icon);
        if (icon.AssetType != GameAssetType.Icon)
        {
            throw new ArgumentException("只能通过此接口保存图标资产。", nameof(icon));
        }

        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (var deletePrevious = connection.CreateCommand())
        {
            deletePrevious.Transaction = (SqliteTransaction)transaction;
            deletePrevious.CommandText =
                "DELETE FROM GameAssets WHERE GameId = $gameId AND AssetType = 'Icon' AND Id <> $id;";
            deletePrevious.Parameters.AddWithValue("$gameId", icon.GameId.ToString("D"));
            deletePrevious.Parameters.AddWithValue("$id", icon.Id.ToString("D"));
            await deletePrevious.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await UpsertAssetAsync(connection, (SqliteTransaction)transaction, icon, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetCoverAsync(
        GameAsset cover,
        bool isUserEdited,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cover);
        if (cover.AssetType != GameAssetType.Cover)
        {
            throw new ArgumentException("只能通过此接口保存封面资产。", nameof(cover));
        }

        var game = await GetByIdAsync(cover.GameId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("找不到要设置封面的游戏。");
        var editedFields = isUserEdited
            ? game.UserEditedFields.Append(GameEditableField.Cover)
            : game.UserEditedFields;

        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await DeleteOtherAssetsAsync(
            connection,
            (SqliteTransaction)transaction,
            cover.GameId,
            GameAssetType.Cover,
            cover.Id,
            cancellationToken).ConfigureAwait(false);
        await UpsertAssetAsync(connection, (SqliteTransaction)transaction, cover, cancellationToken)
            .ConfigureAwait(false);
        await UpdateEditedFieldsAsync(
            connection,
            (SqliteTransaction)transaction,
            cover.GameId,
            editedFields,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveCoverAsync(Guid gameId, CancellationToken cancellationToken)
    {
        var game = await GetByIdAsync(gameId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("找不到要移除封面的游戏。");
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "DELETE FROM GameAssets WHERE GameId = $gameId AND AssetType = 'Cover';";
            command.Parameters.AddWithValue("$gameId", gameId.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await UpdateEditedFieldsAsync(
            connection,
            (SqliteTransaction)transaction,
            gameId,
            game.UserEditedFields.Append(GameEditableField.Cover),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetAvailabilityByVolumeAsync(
        string volumeIdentity,
        GameAvailability availability,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeIdentity);
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE Games SET Availability = $availability WHERE VolumeIdentity = $volumeIdentity;";
        command.Parameters.AddWithValue("$availability", availability.ToString());
        command.Parameters.AddWithValue("$volumeIdentity", volumeIdentity);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RebindVolumeAsync(
        string volumeIdentity,
        string previousRoot,
        string currentRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(previousRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentRoot);
        var normalizedPrevious = Path.TrimEndingDirectorySeparator(Path.GetFullPath(previousRoot));
        var normalizedCurrent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentRoot));
        if (normalizedPrevious.Equals(normalizedCurrent, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var sql in new[]
                 {
                     "UPDATE Games SET InstallRoot = $currentRoot || substr(InstallRoot, length($previousRoot) + 1) WHERE VolumeIdentity = $volumeIdentity AND InstallRoot LIKE $previousPrefix COLLATE NOCASE;",
                     "UPDATE LaunchProfiles SET ExecutablePath = $currentRoot || substr(ExecutablePath, length($previousRoot) + 1), WorkingDirectory = CASE WHEN WorkingDirectory LIKE $previousPrefix COLLATE NOCASE THEN $currentRoot || substr(WorkingDirectory, length($previousRoot) + 1) ELSE WorkingDirectory END WHERE GameId IN (SELECT Id FROM Games WHERE VolumeIdentity = $volumeIdentity) AND ExecutablePath LIKE $previousPrefix COLLATE NOCASE;",
                     "UPDATE ScanCandidates SET ExecutablePath = $currentRoot || substr(ExecutablePath, length($previousRoot) + 1), WorkingDirectory = CASE WHEN WorkingDirectory LIKE $previousPrefix COLLATE NOCASE THEN $currentRoot || substr(WorkingDirectory, length($previousRoot) + 1) ELSE WorkingDirectory END, InstallRoot = CASE WHEN InstallRoot LIKE $previousPrefix COLLATE NOCASE THEN $currentRoot || substr(InstallRoot, length($previousRoot) + 1) ELSE InstallRoot END WHERE VolumeIdentity = $volumeIdentity AND ExecutablePath LIKE $previousPrefix COLLATE NOCASE;",
                 })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$volumeIdentity", volumeIdentity);
            command.Parameters.AddWithValue("$previousRoot", normalizedPrevious);
            command.Parameters.AddWithValue("$previousPrefix", normalizedPrevious + "%");
            command.Parameters.AddWithValue("$currentRoot", normalizedCurrent);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SqliteCommand CreateSelectCommand(SqliteConnection connection, string? predicate)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            $$"""
            SELECT
                g.Id, g.Title, g.Description, g.InstallRoot, g.SourceType,
                g.SourceGameId, g.VolumeIdentity, g.DetectionConfidence,
                g.IsFavorite, g.Availability, g.DateAddedUtc, g.LastPlayedUtc,
                g.TotalPlaySeconds, g.UserEditedFields,
                g.MetadataProviderId, g.MetadataSourceId, g.MetadataSourceName, g.MetadataUpdatedAtUtc,
                lp.Id, lp.Name, lp.LaunchKind, lp.ExecutablePath, lp.Arguments,
                lp.WorkingDirectory, lp.RunAsAdministrator, lp.IsDefault,
                lp.ExpectedProcessNames, lp.GracefulStopTimeoutSeconds,
                icon.Id, icon.LocalPath, icon.Source, icon.Width, icon.Height,
                icon.ContentHash, icon.UpdatedAtUtc,
                cover.Id, cover.LocalPath, cover.Source, cover.Width, cover.Height,
                cover.ContentHash, cover.UpdatedAtUtc,
                hero.Id, hero.LocalPath, hero.Source, hero.Width, hero.Height,
                hero.ContentHash, hero.UpdatedAtUtc
            FROM Games g
            INNER JOIN LaunchProfiles lp ON lp.GameId = g.Id AND lp.IsDefault = 1
            LEFT JOIN GameAssets icon ON icon.GameId = g.Id AND icon.AssetType = 'Icon'
            LEFT JOIN GameAssets cover ON cover.GameId = g.Id AND cover.AssetType = 'Cover'
            LEFT JOIN GameAssets hero ON hero.GameId = g.Id AND hero.AssetType = 'Hero'
            {{(predicate is null ? string.Empty : $"WHERE {predicate}")}}
            ORDER BY g.SortTitle;
            """;
        return command;
    }

    private static Game ReadGame(SqliteDataReader reader)
    {
        var gameId = Guid.Parse(reader.GetString(0));
        var profile = new LaunchProfile(
            Guid.Parse(reader.GetString(18)),
            gameId,
            reader.GetString(19),
            Enum.Parse<LaunchKind>(reader.GetString(20), ignoreCase: false),
            reader.GetString(21),
            reader.IsDBNull(22) ? null : reader.GetString(22),
            reader.GetString(23),
            reader.GetBoolean(24),
            reader.GetBoolean(25),
            JsonSerializer.Deserialize<string[]>(reader.GetString(26)) ?? [],
            reader.GetInt32(27));
        var icon = ReadAsset(reader, 28, gameId, GameAssetType.Icon);
        var cover = ReadAsset(reader, 35, gameId, GameAssetType.Cover);
        var hero = ReadAsset(reader, 42, gameId, GameAssetType.Hero);
        var editedFields = JsonSerializer.Deserialize<GameEditableField[]>(reader.GetString(13)) ?? [];
        var attribution = reader.IsDBNull(14)
            ? null
            : new GameMetadataAttribution(
                reader.GetString(14),
                reader.GetString(15),
                reader.GetString(16),
                DateTimeOffset.Parse(reader.GetString(17), null, System.Globalization.DateTimeStyles.RoundtripKind));

        return new Game(
            gameId,
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            Enum.Parse<GameSourceType>(reader.GetString(4), ignoreCase: false),
            reader.GetBoolean(8),
            Enum.Parse<GameAvailability>(reader.GetString(9), ignoreCase: false),
            DateTimeOffset.Parse(reader.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind),
            reader.IsDBNull(11)
                ? null
                : DateTimeOffset.Parse(reader.GetString(11), null, System.Globalization.DateTimeStyles.RoundtripKind),
            reader.GetInt64(12),
            profile,
            icon,
            reader.IsDBNull(5) && reader.IsDBNull(6)
                ? null
                : new GameDiscoveryMetadata(
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetInt32(7)),
            cover,
            editedFields,
            attribution,
            hero);
    }

    private static async Task InsertGameAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Game game,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Games (
                Id, Title, SortTitle, Description, InstallRoot, SourceType,
                SourceGameId, VolumeIdentity, IsFavorite, IsHidden, Availability,
                DetectionConfidence, UserEditedFields, DateAddedUtc, LastPlayedUtc,
                TotalPlaySeconds, MetadataProviderId, MetadataSourceId,
                MetadataSourceName, MetadataUpdatedAtUtc)
            VALUES (
                $id, $title, $sortTitle, $description, $installRoot, $sourceType,
                $sourceGameId, $volumeIdentity, $isFavorite, 0, $availability,
                $detectionConfidence, $userEditedFields, $dateAddedUtc, $lastPlayedUtc,
                $totalPlaySeconds, $metadataProviderId, $metadataSourceId,
                $metadataSourceName, $metadataUpdatedAtUtc);
            """;
        AddGameParameters(command, game);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertLaunchProfileAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LaunchProfile profile,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO LaunchProfiles (
                Id, GameId, Name, LaunchKind, ExecutablePath, Arguments,
                WorkingDirectory, RunAsAdministrator, ExpectedProcessNames,
                IsDefault, GracefulStopTimeoutSeconds)
            VALUES (
                $id, $gameId, $name, $launchKind, $executablePath, $arguments,
                $workingDirectory, $runAsAdministrator, $expectedProcessNames,
                $isDefault, $gracefulStopTimeoutSeconds);
            """;
        AddLaunchProfileParameters(command, profile);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertAssetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GameAsset asset,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO GameAssets (
                Id, GameId, AssetType, LocalPath, Source, Width, Height,
                ContentHash, UpdatedAtUtc)
            VALUES (
                $id, $gameId, $assetType, $localPath, $source, $width, $height,
                $contentHash, $updatedAtUtc)
            ON CONFLICT(Id) DO UPDATE SET
                LocalPath = excluded.LocalPath,
                Source = excluded.Source,
                Width = excluded.Width,
                Height = excluded.Height,
                ContentHash = excluded.ContentHash,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$id", asset.Id.ToString("D"));
        command.Parameters.AddWithValue("$gameId", asset.GameId.ToString("D"));
        command.Parameters.AddWithValue("$assetType", asset.AssetType.ToString());
        command.Parameters.AddWithValue("$localPath", asset.LocalPath);
        command.Parameters.AddWithValue("$source", asset.Source);
        command.Parameters.AddWithValue("$width", asset.Width);
        command.Parameters.AddWithValue("$height", asset.Height);
        command.Parameters.AddWithValue("$contentHash", (object?)asset.ContentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAtUtc", asset.UpdatedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddGameParameters(SqliteCommand command, Game game)
    {
        command.Parameters.AddWithValue("$id", game.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", game.Title);
        command.Parameters.AddWithValue("$sortTitle", game.SortTitle);
        command.Parameters.AddWithValue("$description", (object?)game.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$installRoot", game.InstallRoot);
        command.Parameters.AddWithValue("$sourceType", game.SourceType.ToString());
        command.Parameters.AddWithValue(
            "$sourceGameId",
            (object?)game.DiscoveryMetadata?.SourceGameId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$volumeIdentity",
            (object?)game.DiscoveryMetadata?.VolumeIdentity ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$detectionConfidence",
            game.DiscoveryMetadata?.DetectionConfidence ?? 100);
        command.Parameters.AddWithValue("$isFavorite", game.IsFavorite);
        command.Parameters.AddWithValue("$availability", game.Availability.ToString());
        command.Parameters.AddWithValue("$dateAddedUtc", game.DateAddedUtc.ToString("O"));
        command.Parameters.AddWithValue("$lastPlayedUtc", game.LastPlayedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$totalPlaySeconds", game.TotalPlaySeconds);
        command.Parameters.AddWithValue(
            "$userEditedFields",
            JsonSerializer.Serialize(game.UserEditedFields.OrderBy(static field => field)));
        command.Parameters.AddWithValue(
            "$metadataProviderId",
            (object?)game.MetadataAttribution?.ProviderId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$metadataSourceId",
            (object?)game.MetadataAttribution?.SourceId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$metadataSourceName",
            (object?)game.MetadataAttribution?.SourceName ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$metadataUpdatedAtUtc",
            game.MetadataAttribution?.UpdatedAtUtc.ToString("O") ?? (object)DBNull.Value);
    }

    private static GameAsset? ReadAsset(
        SqliteDataReader reader,
        int startIndex,
        Guid gameId,
        GameAssetType assetType) =>
        reader.IsDBNull(startIndex)
            ? null
            : new GameAsset(
                Guid.Parse(reader.GetString(startIndex)),
                gameId,
                assetType,
                reader.GetString(startIndex + 1),
                reader.GetString(startIndex + 2),
                reader.GetInt32(startIndex + 3),
                reader.GetInt32(startIndex + 4),
                DateTimeOffset.Parse(
                    reader.GetString(startIndex + 6),
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                reader.IsDBNull(startIndex + 5) ? null : reader.GetString(startIndex + 5));

    private static async Task DeleteOtherAssetsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid gameId,
        GameAssetType assetType,
        Guid retainedAssetId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM GameAssets WHERE GameId = $gameId AND AssetType = $assetType AND Id <> $id;";
        command.Parameters.AddWithValue("$gameId", gameId.ToString("D"));
        command.Parameters.AddWithValue("$assetType", assetType.ToString());
        command.Parameters.AddWithValue("$id", retainedAssetId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpdateEditedFieldsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid gameId,
        IEnumerable<GameEditableField> fields,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Games SET UserEditedFields = $fields WHERE Id = $gameId;";
        command.Parameters.AddWithValue(
            "$fields",
            JsonSerializer.Serialize(fields.Distinct().OrderBy(static field => field)));
        command.Parameters.AddWithValue("$gameId", gameId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddLaunchProfileParameters(SqliteCommand command, LaunchProfile profile)
    {
        command.Parameters.AddWithValue("$id", profile.Id.ToString("D"));
        command.Parameters.AddWithValue("$gameId", profile.GameId.ToString("D"));
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$launchKind", profile.LaunchKind.ToString());
        command.Parameters.AddWithValue("$executablePath", profile.ExecutablePath);
        command.Parameters.AddWithValue("$arguments", (object?)profile.Arguments ?? DBNull.Value);
        command.Parameters.AddWithValue("$workingDirectory", profile.WorkingDirectory);
        command.Parameters.AddWithValue("$runAsAdministrator", profile.RunAsAdministrator);
        command.Parameters.AddWithValue(
            "$expectedProcessNames",
            JsonSerializer.Serialize(profile.ExpectedProcessNames));
        command.Parameters.AddWithValue("$gracefulStopTimeoutSeconds", profile.GracefulStopTimeoutSeconds);
        command.Parameters.AddWithValue("$isDefault", profile.IsDefault);
    }
}
