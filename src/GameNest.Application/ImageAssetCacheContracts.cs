namespace GameNest.Application;

public enum CachedImageKind
{
    Icon,
    Cover,
}

public sealed record CachedImageAsset(
    string LocalPath,
    string ContentHash,
    int Width,
    int Height);

public interface IImageAssetCache
{
    Task<CachedImageAsset?> CacheAsync(
        string sourcePath,
        CachedImageKind kind,
        uint requestedSize,
        CancellationToken cancellationToken);
}
