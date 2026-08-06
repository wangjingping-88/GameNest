using System.Text.Json;
using GameNest.Application;

namespace GameNest.Infrastructure.Persistence;

public sealed class SqliteUpdatePreferenceStore(
    GameNestDataPaths paths,
    IApplicationDataInitializer initializer) : IUpdatePreferenceStore
{
    private const string PreferenceKey = "updates.preference";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<UpdatePreference> GetAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", PreferenceKey);

        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(value))
        {
            return UpdatePreference.Default;
        }

        try
        {
            return JsonSerializer.Deserialize<UpdatePreference>(value, JsonOptions)
                   ?? UpdatePreference.Default;
        }
        catch (JsonException)
        {
            return UpdatePreference.Default;
        }
    }

    public async Task SetAsync(UpdatePreference preference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preference);
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO AppSettings (Key, Value, UpdatedAtUtc)
            VALUES ($key, $value, $updatedAtUtc)
            ON CONFLICT(Key) DO UPDATE SET
                Value = excluded.Value,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$key", PreferenceKey);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(preference, JsonOptions));
        command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
