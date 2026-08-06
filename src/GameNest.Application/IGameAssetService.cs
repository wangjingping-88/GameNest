using GameNest.Domain;

namespace GameNest.Application;

public interface IGameAssetService
{
    Task<GameAsset?> ExtractIconAsync(
        Guid gameId,
        LocalGameFileInspection inspection,
        CancellationToken cancellationToken);

    Task<GameAsset?> DiscoverCoverAsync(
        Guid gameId,
        string installRoot,
        CancellationToken cancellationToken);

    Task<GameAsset> ImportCoverAsync(
        Guid gameId,
        string sourcePath,
        CancellationToken cancellationToken);

    Task<GameAsset> ImportCoverFromUriAsync(
        Guid gameId,
        Uri sourceUri,
        string sourceName,
        CancellationToken cancellationToken) =>
        Task.FromException<GameAsset>(
            new NotSupportedException("当前资产服务不支持下载在线封面。"));
}
