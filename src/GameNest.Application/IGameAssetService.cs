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
}
