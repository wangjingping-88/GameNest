using GameNest.Application;
using GameNest.Infrastructure;
using GameNest.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class SqliteThemePreferenceStoreTests
{
    [Fact]
    public async Task StoreDefaultsToLightAndRoundTripsSelection()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        using var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        var store = new SqliteThemePreferenceStore(paths, initializer);

        var initial = await store.GetAsync(TestContext.Current.CancellationToken);
        await store.SetAsync(ThemePreference.System, TestContext.Current.CancellationToken);
        var saved = await store.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ThemePreference.Light, initial);
        Assert.Equal(ThemePreference.System, saved);
    }
}
