using System.Net;
using System.Text;
using GameNest.Infrastructure;

namespace GameNest.Infrastructure.Tests;

public sealed class SteamStoreCoverSearchProviderTests
{
    [Fact]
    public async Task SearchReturnsSteamCoverCandidatesAndMarksExactTitle()
    {
        using var client = new HttpClient(new StaticResponseHandler("""
            {"items":[{"id":123,"name":"Hades","type":"app"},{"id":456,"name":"Hades II","type":"app"}]}
            """));
        var provider = new SteamStoreCoverSearchProvider(client);

        var candidates = await provider.SearchAsync("Hades", TestContext.Current.CancellationToken);

        Assert.Equal(2, candidates.Count);
        Assert.True(candidates[0].IsExactTitleMatch);
        Assert.Equal("https://cdn.cloudflare.steamstatic.com/steam/apps/123/library_600x900.jpg", candidates[0].ImageUri.AbsoluteUri);
        Assert.False(candidates[1].IsExactTitleMatch);
    }

    private sealed class StaticResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Contains("storesearch", request.RequestUri!.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
