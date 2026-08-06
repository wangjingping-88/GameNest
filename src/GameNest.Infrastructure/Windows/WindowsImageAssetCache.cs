using GameNest.Application;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace GameNest.Infrastructure.Windows;

public sealed class WindowsImageAssetCache(GameNestDataPaths paths) : IImageAssetCache
{
    public async Task<CachedImageAsset?> CacheAsync(
        string sourcePath,
        CachedImageKind kind,
        uint requestedSize,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentOutOfRangeException.ThrowIfZero(requestedSize);

        var cacheDirectory = Path.Combine(paths.AssetDirectory, "cache");
        await Task.Run(() => Directory.CreateDirectory(cacheDirectory), cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(sourcePath))
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            var thumbnailMode = kind == CachedImageKind.Cover
                ? ThumbnailMode.PicturesView
                : ThumbnailMode.SingleItem;
            var options = kind == CachedImageKind.Cover
                ? ThumbnailOptions.ResizeThumbnail
                : ThumbnailOptions.UseCurrentScale;
            using var thumbnail = await storageFile
                .GetThumbnailAsync(thumbnailMode, requestedSize, options)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            if (thumbnail.Size == 0)
            {
                return null;
            }

            await using var source = thumbnail.AsStreamForRead();
            using var buffer = new MemoryStream(checked((int)thumbnail.Size));
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            var contentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var extension = thumbnail.ContentType switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                _ => ".bmp",
            };
            var localPath = Path.Combine(
                cacheDirectory,
                $"{contentHash}-{kind.ToString().ToLowerInvariant()}-{requestedSize}{extension}");
            if (!File.Exists(localPath))
            {
                await File.WriteAllBytesAsync(localPath, bytes, cancellationToken).ConfigureAwait(false);
            }

            return new CachedImageAsset(
                localPath,
                contentHash,
                checked((int)thumbnail.OriginalWidth),
                checked((int)thumbnail.OriginalHeight));
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            exception is UnauthorizedAccessException or FileNotFoundException or InvalidOperationException or COMException)
        {
            return null;
        }
    }
}
