using GameNest.Domain;

namespace GameNest.Application;

public interface IOverlayProfileRepository
{
    Task<OverlayProfile> GetGlobalAsync(CancellationToken cancellationToken);

    Task<OverlayProfile?> GetForGameAsync(Guid gameId, CancellationToken cancellationToken);

    Task SaveAsync(OverlayProfile profile, CancellationToken cancellationToken);

    Task RemoveForGameAsync(Guid gameId, CancellationToken cancellationToken);
}
