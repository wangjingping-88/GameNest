using GameNest.Application;
using GameNest.Domain;
using Microsoft.Extensions.Logging;

namespace GameNest.Infrastructure.Windows;

public sealed class WindowsGameAssetService(
    IImageAssetCache imageCache,
    ILogger<WindowsGameAssetService> logger) : IGameAssetService
{
    private static readonly HashSet<string> SupportedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

    private static readonly string[] PreferredCoverNames =
        ["cover", "poster", "folder", "background", "banner"];

    private static readonly Action<ILogger, string, Exception?> AssetExtractionFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1100, nameof(AssetExtractionFailed)),
            "无法处理 {FileName} 的本地图片，将使用占位封面。");

    public async Task<GameAsset?> ExtractIconAsync(
        Guid gameId,
        LocalGameFileInspection inspection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        try
        {
            var cached = await imageCache
                .CacheAsync(inspection.IconSourcePath, CachedImageKind.Icon, 256, cancellationToken)
                .ConfigureAwait(false);
            return cached is null
                ? null
                : CreateAsset(gameId, GameAssetType.Icon, inspection.IconSourcePath, "LocalIcon", cached);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AssetExtractionFailed(logger, Path.GetFileName(inspection.IconSourcePath), exception);
            return null;
        }
    }

    public async Task<GameAsset?> DiscoverCoverAsync(
        Guid gameId,
        string installRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        var sourcePath = await Task.Run(
                () => FindCoverCandidate(installRoot),
                cancellationToken)
            .ConfigureAwait(false);
        if (sourcePath is null)
        {
            return null;
        }

        try
        {
            var cached = await imageCache
                .CacheAsync(sourcePath, CachedImageKind.Cover, 900, cancellationToken)
                .ConfigureAwait(false);
            return cached is null
                ? null
                : CreateAsset(gameId, GameAssetType.Cover, sourcePath, "LocalDiscovery", cached);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AssetExtractionFailed(logger, Path.GetFileName(sourcePath), exception);
            return null;
        }
    }

    public async Task<GameAsset> ImportCoverAsync(
        Guid gameId,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!SupportedImageExtensions.Contains(Path.GetExtension(sourcePath)))
        {
            throw new NotSupportedException("封面仅支持 PNG、JPG、JPEG、BMP 或 WebP 图片。");
        }

        var cached = await imageCache
            .CacheAsync(sourcePath, CachedImageKind.Cover, 900, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("无法读取所选封面，请确认图片未损坏且仍可访问。");
        return CreateAsset(gameId, GameAssetType.Cover, sourcePath, "UserImport", cached);
    }

    private static GameAsset CreateAsset(
        Guid gameId,
        GameAssetType assetType,
        string sourcePath,
        string source,
        CachedImageAsset cached) =>
        new(
            Guid.NewGuid(),
            gameId,
            assetType,
            cached.LocalPath,
            $"{source}:{sourcePath}",
            cached.Width,
            cached.Height,
            DateTimeOffset.UtcNow,
            cached.ContentHash);

    private static string? FindCoverCandidate(string installRoot)
    {
        if (!Directory.Exists(installRoot))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(installRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedImageExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new
            {
                Path = path,
                Score = Array.FindIndex(
                    PreferredCoverNames,
                    preferred => string.Equals(
                        Path.GetFileNameWithoutExtension(path),
                        preferred,
                        StringComparison.OrdinalIgnoreCase)),
            })
            .Where(static item => item.Score >= 0)
            .OrderBy(static item => item.Score)
            .ThenBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Path)
            .FirstOrDefault();
    }
}
