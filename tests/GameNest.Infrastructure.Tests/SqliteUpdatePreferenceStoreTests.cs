using GameNest.Application;
using GameNest.Infrastructure;
using GameNest.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class SqliteUpdatePreferenceStoreTests
{
    [Fact]
    public async Task StoreDefaultsToAutomaticAndRoundTripsCacheMetadata()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        var store = new SqliteUpdatePreferenceStore(paths, initializer);
        var checkedAt = new DateTimeOffset(2026, 8, 6, 1, 2, 3, TimeSpan.Zero);

        var initial = await store.GetAsync(TestContext.Current.CancellationToken);
        await store.SetAsync(new UpdatePreference(false, checkedAt, "\"etag-1\""), TestContext.Current.CancellationToken);
        var saved = await store.GetAsync(TestContext.Current.CancellationToken);

        Assert.True(initial.AutomaticCheckEnabled);
        Assert.Equal(new UpdatePreference(false, checkedAt, "\"etag-1\""), saved);
    }
}
