using GameNest.Domain;

namespace GameNest.Application;

public interface IGameLaunchService
{
    event EventHandler<GameProcessStatusChangedEventArgs>? StatusChanged;

    bool IsRunning(Guid gameId);

    GameRuntimeSnapshot? GetRuntime(Guid gameId);

    Task<GameLaunchResult> LaunchAsync(Game game, CancellationToken cancellationToken);

    Task<GameStopResult> StopAsync(
        Guid gameId,
        bool force,
        CancellationToken cancellationToken);
}
