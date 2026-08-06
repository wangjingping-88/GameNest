using GameNest.Domain;

namespace GameNest.Application;

public sealed record GameLaunchResult(
    Guid GameId,
    int ProcessId,
    GameRuntimeState State,
    GameProcessConfidence Confidence);

public sealed class GameProcessStatusChangedEventArgs(
    GameRuntimeSnapshot runtime) : EventArgs
{
    public GameRuntimeSnapshot Runtime { get; } = runtime;

    public Guid GameId => Runtime.GameId;

    public int? ProcessId => Runtime.PrimaryProcessId;

    public GameRuntimeState State => Runtime.State;
}
