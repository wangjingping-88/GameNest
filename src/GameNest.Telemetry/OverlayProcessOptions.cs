namespace GameNest.Telemetry;

public sealed record OverlayProcessOptions(string ExecutablePath, TimeSpan ConnectionTimeout)
{
    public static OverlayProcessOptions CreateDefault() =>
        new(
            Path.Combine(AppContext.BaseDirectory, "Overlay", "GameNest.Overlay.exe"),
            TimeSpan.FromSeconds(5));
}
