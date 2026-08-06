using System.Text.Json;
using GameNest.Application;

namespace GameNest.Infrastructure;

/// <summary>查询 Steam 商店公开搜索接口，不保存用户凭据。</summary>
public sealed class SteamStoreCoverSearchProvider(HttpClient httpClient) : IGameCoverSearchProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<GameCoverCandidate>> SearchAsync(
        string title,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var normalized = Normalize(title);
        var endpoint = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(title)}&l=schinese&cc=CN";
        using var response = await httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<SteamSearchResponse>(body, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return result?.Items?
            .Where(static item => item.Type is null or "app")
            .Where(static item => item.Id > 0 && !string.IsNullOrWhiteSpace(item.Name))
            .Take(8)
            .Select(item => new GameCoverCandidate(
                item.Name!,
                new Uri($"https://cdn.cloudflare.steamstatic.com/steam/apps/{item.Id}/library_600x900.jpg"),
                "Steam 商店",
                string.Equals(Normalize(item.Name!), normalized, StringComparison.Ordinal)))
            .ToArray()
            ?? [];
    }

    private static string Normalize(string value) => new(
        value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private sealed record SteamSearchResponse(IReadOnlyList<SteamSearchItem>? Items);

    private sealed record SteamSearchItem(int Id, string? Name, string? Type);
}
