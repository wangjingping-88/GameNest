using GameNest.Domain;

namespace GameNest.Application;

public interface IGameLibraryRepository
{
    Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken);

    Task<Game?> GetByIdAsync(Guid gameId, CancellationToken cancellationToken);

    Task<Game?> FindByExecutablePathAsync(string executablePath, CancellationToken cancellationToken);

    Task AddAsync(Game game, CancellationToken cancellationToken);

    Task UpdateAsync(Game game, CancellationToken cancellationToken);

    Task SetIconAsync(GameAsset icon, CancellationToken cancellationToken);

    Task SetCoverAsync(GameAsset cover, bool isUserEdited, CancellationToken cancellationToken);

    Task RemoveCoverAsync(Guid gameId, CancellationToken cancellationToken);

    Task SetAvailabilityByVolumeAsync(
        string volumeIdentity,
        GameAvailability availability,
        CancellationToken cancellationToken);

    Task RebindVolumeAsync(
        string volumeIdentity,
        string previousRoot,
        string currentRoot,
        CancellationToken cancellationToken);

    Task<bool> RemoveAsync(Guid gameId, CancellationToken cancellationToken);
}
