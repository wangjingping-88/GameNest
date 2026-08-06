using GameNest.Application;
using GameNest.Domain;
using Microsoft.Data.Sqlite;

namespace GameNest.Infrastructure.Persistence;

public sealed class SqliteGameMetadataRepository(
    GameNestDataPaths paths,
    IApplicationDataInitializer initializer,
    IGameLibraryRepository gameRepository) : IGameMetadataRepository
{
    public async Task ApplyAsync(
        Game original,
        Game updated,
        MetadataCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(updated);
        ArgumentNullException.ThrowIfNull(candidate);
        if (original.Id != updated.Id)
        {
            throw new ArgumentException("元数据更新前后的游戏 ID 必须一致。", nameof(updated));
        }

        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var revisionCommand = connection.CreateCommand())
        {
            revisionCommand.Transaction = (SqliteTransaction)transaction;
            revisionCommand.CommandText =
                """
                INSERT INTO MetadataRevisions (
                    Id, GameId, PreviousTitle, PreviousDescription,
                    PreviousProviderId, PreviousSourceId, PreviousSourceName,
                    PreviousUpdatedAtUtc, AppliedProviderId, AppliedSourceId,
                    AppliedAtUtc, UndoneAtUtc)
                VALUES (
                    $id, $gameId, $previousTitle, $previousDescription,
                    $previousProviderId, $previousSourceId, $previousSourceName,
                    $previousUpdatedAtUtc, $appliedProviderId, $appliedSourceId,
                    $appliedAtUtc, NULL);
                """;
            revisionCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            revisionCommand.Parameters.AddWithValue("$gameId", original.Id.ToString("D"));
            revisionCommand.Parameters.AddWithValue("$previousTitle", original.Title);
            revisionCommand.Parameters.AddWithValue(
                "$previousDescription",
                (object?)original.Description ?? DBNull.Value);
            revisionCommand.Parameters.AddWithValue(
                "$previousProviderId",
                (object?)original.MetadataAttribution?.ProviderId ?? DBNull.Value);
            revisionCommand.Parameters.AddWithValue(
                "$previousSourceId",
                (object?)original.MetadataAttribution?.SourceId ?? DBNull.Value);
            revisionCommand.Parameters.AddWithValue(
                "$previousSourceName",
                (object?)original.MetadataAttribution?.SourceName ?? DBNull.Value);
            revisionCommand.Parameters.AddWithValue(
                "$previousUpdatedAtUtc",
                original.MetadataAttribution?.UpdatedAtUtc.ToString("O") ?? (object)DBNull.Value);
            revisionCommand.Parameters.AddWithValue("$appliedProviderId", candidate.ProviderId);
            revisionCommand.Parameters.AddWithValue("$appliedSourceId", candidate.SourceId);
            revisionCommand.Parameters.AddWithValue("$appliedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            await revisionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var updateCommand = connection.CreateCommand())
        {
            updateCommand.Transaction = (SqliteTransaction)transaction;
            updateCommand.CommandText =
                """
                UPDATE Games SET
                    Title = $title,
                    SortTitle = $sortTitle,
                    Description = $description,
                    MetadataProviderId = $providerId,
                    MetadataSourceId = $sourceId,
                    MetadataSourceName = $sourceName,
                    MetadataUpdatedAtUtc = $updatedAtUtc
                WHERE Id = $gameId;
                """;
            updateCommand.Parameters.AddWithValue("$title", updated.Title);
            updateCommand.Parameters.AddWithValue("$sortTitle", updated.SortTitle);
            updateCommand.Parameters.AddWithValue(
                "$description",
                (object?)updated.Description ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue(
                "$providerId",
                (object?)updated.MetadataAttribution?.ProviderId ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue(
                "$sourceId",
                (object?)updated.MetadataAttribution?.SourceId ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue(
                "$sourceName",
                (object?)updated.MetadataAttribution?.SourceName ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue(
                "$updatedAtUtc",
                updated.MetadataAttribution?.UpdatedAtUtc.ToString("O") ?? (object)DBNull.Value);
            updateCommand.Parameters.AddWithValue("$gameId", updated.Id.ToString("D"));
            if (await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new KeyNotFoundException("找不到要应用元数据的游戏。");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Game?> UndoLastAsync(Guid gameId, CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        MetadataRevision? revision;
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = (SqliteTransaction)transaction;
            selectCommand.CommandText =
                """
                SELECT Id, PreviousTitle, PreviousDescription, PreviousProviderId,
                       PreviousSourceId, PreviousSourceName, PreviousUpdatedAtUtc
                FROM MetadataRevisions
                WHERE GameId = $gameId AND UndoneAtUtc IS NULL
                ORDER BY AppliedAtUtc DESC, rowid DESC
                LIMIT 1;
                """;
            selectCommand.Parameters.AddWithValue("$gameId", gameId.ToString("D"));
            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            revision = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? new MetadataRevision(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6))
                : null;
        }

        if (revision is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        await using (var updateCommand = connection.CreateCommand())
        {
            updateCommand.Transaction = (SqliteTransaction)transaction;
            updateCommand.CommandText =
                """
                UPDATE Games SET
                    Title = $title,
                    SortTitle = $sortTitle,
                    Description = $description,
                    MetadataProviderId = $providerId,
                    MetadataSourceId = $sourceId,
                    MetadataSourceName = $sourceName,
                    MetadataUpdatedAtUtc = $updatedAtUtc
                WHERE Id = $gameId;

                UPDATE MetadataRevisions
                SET UndoneAtUtc = $undoneAtUtc
                WHERE Id = $revisionId;
                """;
            updateCommand.Parameters.AddWithValue("$title", revision.Title);
            updateCommand.Parameters.AddWithValue("$sortTitle", revision.Title.ToUpperInvariant());
            updateCommand.Parameters.AddWithValue(
                "$description",
                (object?)revision.Description ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("$providerId", (object?)revision.ProviderId ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("$sourceId", (object?)revision.SourceId ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("$sourceName", (object?)revision.SourceName ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("$updatedAtUtc", (object?)revision.UpdatedAtUtc ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("$gameId", gameId.ToString("D"));
            updateCommand.Parameters.AddWithValue("$undoneAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            updateCommand.Parameters.AddWithValue("$revisionId", revision.Id);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await gameRepository.GetByIdAsync(gameId, cancellationToken).ConfigureAwait(false);
    }

    private sealed record MetadataRevision(
        string Id,
        string Title,
        string? Description,
        string? ProviderId,
        string? SourceId,
        string? SourceName,
        string? UpdatedAtUtc);
}
