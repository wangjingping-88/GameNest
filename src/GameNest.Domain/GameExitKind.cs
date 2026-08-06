namespace GameNest.Domain;

public enum GameExitKind
{
    Natural,
    Graceful,
    Forced,
    TrackingLost,
    ApplicationClosed,
}
