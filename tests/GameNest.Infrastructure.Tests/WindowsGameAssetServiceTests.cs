using GameNest.Application;
using GameNest.Domain;
using GameNest.Infrastructure;
using GameNest.Infrastructure.Windows;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class WindowsGameAssetServiceTests
{
    [Fact]
    public async Task CacheUsesContentAddressedPathForRepeatedIconRequest()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        var cache = new WindowsImageAssetCache(paths);
        var sourcePath = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("测试环境缺少 ComSpec。");

        var first = await cache.CacheAsync(
            sourcePath,
            CachedImageKind.Icon,
            256,
            TestContext.Current.CancellationToken);
        var second = await cache.CacheAsync(
            sourcePath,
            CachedImageKind.Icon,
            256,
            TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.True(File.Exists(first.LocalPath));
        Assert.Contains(first.ContentHash, first.LocalPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractIconAsyncReturnsNullWhenSourceDisappears()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        var service = new WindowsGameAssetService(
            new WindowsImageAssetCache(paths),
            new HttpClient(),
            NullLogger<WindowsGameAssetService>.Instance);
        var missingPath = Path.Combine(directory.Path, "已移除.exe");
        var inspection = new LocalGameFileInspection(
            missingPath,
            missingPath,
            "已移除",
            null,
            directory.Path,
            GameSourceType.ManualExecutable,
            LaunchKind.Executable,
            missingPath);

        var asset = await service.ExtractIconAsync(
            Guid.NewGuid(),
            inspection,
            TestContext.Current.CancellationToken);

        Assert.Null(asset);
    }
}
