using Microsoft.Data.Sqlite;

namespace GameNest.Infrastructure.Persistence;

internal static class SqliteConnectionFactory
{
    public static SqliteConnection Create(GameNestDataPaths paths)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            DefaultTimeout = 15,
        };

        return new SqliteConnection(connectionString.ToString());
    }
}
