namespace GameNest.Domain;

public sealed record GameDiscoveryMetadata(
    string? SourceGameId,
    string? VolumeIdentity,
    int DetectionConfidence)
{
    public int DetectionConfidence { get; } = Math.Clamp(DetectionConfidence, 0, 100);
}
