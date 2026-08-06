namespace GameNest.Domain;

public sealed record GameAsset
{
    public GameAsset(
        Guid id,
        Guid gameId,
        GameAssetType assetType,
        string localPath,
        string source,
        int width,
        int height,
        DateTimeOffset updatedAtUtc,
        string? contentHash = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("资产 ID 不能为空。", nameof(id));
        }

        if (gameId == Guid.Empty)
        {
            throw new ArgumentException("游戏 ID 不能为空。", nameof(gameId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        Id = id;
        GameId = gameId;
        AssetType = assetType;
        LocalPath = localPath;
        Source = source;
        Width = width;
        Height = height;
        UpdatedAtUtc = updatedAtUtc;
        ContentHash = string.IsNullOrWhiteSpace(contentHash) ? null : contentHash.Trim();
    }

    public Guid Id { get; }

    public Guid GameId { get; }

    public GameAssetType AssetType { get; }

    public string LocalPath { get; }

    public string Source { get; }

    public int Width { get; }

    public int Height { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public string? ContentHash { get; }
}
