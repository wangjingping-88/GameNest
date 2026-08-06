using System.Diagnostics;
using GameNest.Application;
using GameNest.Domain;
using GameNest.Infrastructure;
using GameNest.Infrastructure.Persistence;
using GameNest.Infrastructure.Windows;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class GameLibraryAddPerformanceTests
{
    [Fact]
    public async Task ColdAddReturnsBeforeIconExtractionWithinInteractiveBudget()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceExecutable = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("测试环境缺少 ComSpec。");
        var gameDirectory = Path.Combine(directory.Path, "性能 测试 [添加]");
        Directory.CreateDirectory(gameDirectory);
        var executablePath = Path.Combine(gameDirectory, "快速 添加.exe");
        File.Copy(sourceExecutable, executablePath);
        var paths = GameNestDataPaths.CreateForRoot(Path.Combine(directory.Path, "data"));
        using var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        var service = new GameLibraryService(
            new SqliteGameLibraryRepository(paths, initializer),
            new WindowsLocalGameFileInspector(),
            new FailingIfCalledAssetService(),
            new NoOpLaunchService(),
            new StubGameRuntimeRepository());
        var stopwatch = Stopwatch.StartNew();

        var game = await service.AddAsync(
            executablePath,
            new(),
            TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.Equal(Path.GetFullPath(executablePath), game.LaunchProfile.ExecutablePath);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(1500),
            $"冷启动添加耗时 {stopwatch.Elapsed.TotalMilliseconds:F0}ms，超过 1500ms 交互预算。");
    }

    private sealed class FailingIfCalledAssetService : IGameAssetService
    {
        public Task<GameAsset?> ExtractIconAsync(
            Guid gameId,
            LocalGameFileInspection inspection,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("AddAsync 不应同步等待图标提取。");

        public Task<GameAsset?> DiscoverCoverAsync(
            Guid gameId,
            string installRoot,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("AddAsync 不应同步等待封面发现。");

        public Task<GameAsset> ImportCoverAsync(
            Guid gameId,
            string sourcePath,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("AddAsync 不应同步等待封面导入。");
    }

    private sealed class NoOpLaunchService : IGameLaunchService
    {
        public event EventHandler<GameProcessStatusChangedEventArgs>? StatusChanged
        {
            add { }
            remove { }
        }

        public bool IsRunning(Guid gameId) => false;

        public GameRuntimeSnapshot? GetRuntime(Guid gameId) => null;

        public Task<GameLaunchResult> LaunchAsync(Game game, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GameStopResult> StopAsync(
            Guid gameId,
            bool force,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
