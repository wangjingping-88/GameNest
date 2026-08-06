using GameNest.Application;

namespace GameNest.Infrastructure.Persistence;

public sealed class SqliteThemePreferenceStore(
    GameNestDataPaths paths,
    IApplicationDataInitializer initializer) : IThemePreferenceStore
{
    private const string ThemeKey = "appearance.theme";

    public async Task<ThemePreference> GetAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = SqliteConnectionFactory.Create(paths);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", ThemeKey);

        var storedValue = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return ThemePreferenceParser.ParseOrDefault(storedValue);
    }

    public async Task SetAsync(ThemePreference preference, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(preference))
        {
            throw new ArgumentOutOfRangeException(nameof(preference));
        }

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
        command.Parameters.AddWithValue("$key", ThemeKey);
        command.Parameters.AddWithValue("$value", preference.ToString());
        command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
