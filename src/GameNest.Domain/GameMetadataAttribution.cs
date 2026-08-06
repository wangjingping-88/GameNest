namespace GameNest.Domain;

public sealed record GameMetadataAttribution
{
    public GameMetadataAttribution(
        string providerId,
        string sourceId,
        string sourceName,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        ProviderId = providerId.Trim();
        SourceId = sourceId.Trim();
        SourceName = sourceName.Trim();
        UpdatedAtUtc = updatedAtUtc;
    }

    public string ProviderId { get; }

    public string SourceId { get; }

    public string SourceName { get; }

    public DateTimeOffset UpdatedAtUtc { get; }
}
