using GameNest.Application;
using GameNest.Infrastructure.Logging;
using GameNest.Infrastructure.Maintenance;
using GameNest.Infrastructure.Persistence;
using GameNest.Infrastructure.Scanning;
using GameNest.Infrastructure.Updates;
using GameNest.Infrastructure.Windows;
using GameNest.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameNest.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGameNestInfrastructure(
        this IServiceCollection services,
        GameNestDataPaths? dataPaths = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(dataPaths ?? GameNestDataPaths.CreateDefault());
        services.AddSingleton<SqliteDatabaseInitializer>();
        services.AddSingleton<IApplicationDataInitializer>(
            static provider => provider.GetRequiredService<SqliteDatabaseInitializer>());
        services.AddSingleton<IThemePreferenceStore, SqliteThemePreferenceStore>();
        services.AddSingleton<IUpdatePreferenceStore, SqliteUpdatePreferenceStore>();
        services.AddSingleton<IOverlayProfileRepository, SqliteOverlayProfileRepository>();
        services.AddSingleton<IGameLibraryRepository, SqliteGameLibraryRepository>();
        services.AddSingleton<IGameScanRepository, SqliteGameScanRepository>();
        services.AddSingleton<ILocalGameFileInspector, WindowsLocalGameFileInspector>();
        services.AddSingleton<IVolumeIdentityService, WindowsVolumeIdentityService>();
        services.AddSingleton<IGameCandidateScorer, GameCandidateScorer>();
        services.AddSingleton<IGameCandidateGrouper, GameCandidateGrouper>();
        services.AddSingleton<IGameSourceAdapter, SteamGameSourceAdapter>();
        services.AddSingleton<IShortcutSourceLocator, WindowsShortcutSourceLocator>();
        services.AddSingleton<IGameSourceAdapter, ShortcutGameSourceAdapter>();
        services.AddSingleton<IGameSourceAdapter, GenericExecutableGameSourceAdapter>();
        services.AddSingleton<IImageAssetCache, WindowsImageAssetCache>();
        services.AddSingleton<IGameAssetService, WindowsGameAssetService>();
        services.AddSingleton<IGameMetadataRepository, SqliteGameMetadataRepository>();
        services.AddSingleton<IApplicationMaintenanceService, LocalApplicationMaintenanceService>();
        services.AddSingleton(ApplicationUpdateOptions.CreateDefault());
        services.AddSingleton(static _ => new HttpClient(
            new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            })
        {
            Timeout = TimeSpan.FromSeconds(30),
        });
        services.AddSingleton<IApplicationUpdateService, GitHubApplicationUpdateService>();
        services.AddSingleton(PortableUpdateTimingOptions.Default);
        services.AddSingleton<IPortableUpdateApplier, PortableUpdateApplier>();
        services.AddSingleton<IGameRuntimeRepository, SqliteGameRuntimeRepository>();
        services.AddSingleton<IProcessSnapshotProvider, WindowsProcessSnapshotProvider>();
        services.AddSingleton<IGameProcessController, WindowsGameProcessController>();
        services.AddSingleton(GameRuntimeOptions.Default);
        services.AddSingleton<IGameLaunchService, WindowsGameRuntimeService>();
        services.AddSingleton(PresentMonOptions.CreateDefault());
        services.AddSingleton(OverlayProcessOptions.CreateDefault());
        services.AddSingleton<IPerformanceTelemetry, WindowsPerformanceTelemetry>();
        services.AddSingleton<IGameWindowLocator, WindowsGameWindowLocator>();
        services.AddSingleton<IOverlayController, WindowsOverlayController>();
        services.AddSingleton<GameLibraryService>();
        services.AddSingleton<GameMetadataService>();
        services.AddSingleton<GameScanService>();
        services.AddSingleton<OverlaySettingsService>();
        services.AddSingleton<IOverlayRuntimeCoordinator, OverlayRuntimeCoordinator>();
        services.AddSingleton<BackgroundFileLoggerProvider>();
        services.AddSingleton<ILoggerProvider>(
            static provider => provider.GetRequiredService<BackgroundFileLoggerProvider>());

        return services;
    }
}
