using System.Text.Json.Serialization;

namespace GameNest.Infrastructure.Updates;

public sealed record UpdatePackageManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("rid")] string RuntimeIdentifier,
    [property: JsonPropertyName("assetName")] string AssetName,
    [property: JsonPropertyName("size")] long SizeBytes,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("minimumOsBuild")] int MinimumOsBuild,
    [property: JsonPropertyName("publishedAtUtc")] DateTimeOffset PublishedAtUtc,
    [property: JsonPropertyName("keyId")] string KeyId);
