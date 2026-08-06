using System.Net;
using System.Text;
using System.Text.Json;
using GameNest.Application;
using GameNest.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class GitHubApplicationUpdateServiceTests
{
    [Fact]
    public async Task CheckReturnsAvailableForNewStableReleaseWithExactAssets()
    {
        using var directory = TemporaryDirectory.Create();
        using var handler = new QueueMessageHandler(_ => JsonResponse(CreateReleaseJson()));
        using var client = new HttpClient(handler);
        var store = new MemoryUpdatePreferenceStore(UpdatePreference.Default);
        using var service = CreateService(directory.Path, store, client);

        var result = await service.CheckAsync(force: true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateAvailability.Available, result.Availability);
        Assert.Equal(new Version(0, 2, 1), result.Release?.Version);
        Assert.Equal(UpdateInstallCapability.TrustedSigningKeyUnavailable, result.InstallCapability);
        Assert.Equal("GameNest-0.2.1-win-x64-portable.zip", result.Release?.Package.Name);
    }

    [Fact]
    public async Task CheckIgnoresPrereleaseEvenWhenEndpointReturnsIt()
    {
        using var directory = TemporaryDirectory.Create();
        using var handler = new QueueMessageHandler(_ => JsonResponse(CreateReleaseJson(prerelease: true)));
        using var client = new HttpClient(handler);
        using var service = CreateService(directory.Path, new MemoryUpdatePreferenceStore(UpdatePreference.Default), client);

        var result = await service.CheckAsync(force: true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateAvailability.UpToDate, result.Availability);
        Assert.Null(result.Release);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, UpdateAvailability.UpToDate)]
    [InlineData(HttpStatusCode.Forbidden, UpdateAvailability.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, UpdateAvailability.Unavailable)]
    public async Task CheckHandlesExpectedGitHubStatusCodes(
        HttpStatusCode statusCode,
        UpdateAvailability expected)
    {
        using var directory = TemporaryDirectory.Create();
        using var handler = new QueueMessageHandler(_ => new HttpResponseMessage(statusCode));
        using var client = new HttpClient(handler);
        using var service = CreateService(directory.Path, new MemoryUpdatePreferenceStore(UpdatePreference.Default), client);

        var result = await service.CheckAsync(force: true, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Availability);
    }

    [Fact]
    public async Task CheckRejectsReleaseWithMissingSignatureAsset()
    {
        using var directory = TemporaryDirectory.Create();
        using var handler = new QueueMessageHandler(_ => JsonResponse(CreateReleaseJson(includeSignature: false)));
        using var client = new HttpClient(handler);
        using var service = CreateService(directory.Path, new MemoryUpdatePreferenceStore(UpdatePreference.Default), client);

        var result = await service.CheckAsync(force: true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateAvailability.Unavailable, result.Availability);
        Assert.Contains("缺少", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckUsesEtagAndCachedBodyAfterNotModified()
    {
        using var directory = TemporaryDirectory.Create();
        var call = 0;
        using var handler = new QueueMessageHandler(request =>
        {
            call++;
            if (call == 1)
            {
                var response = JsonResponse(CreateReleaseJson());
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"release-1\"");
                return response;
            }

            Assert.Contains(request.Headers.IfNoneMatch, item => item.Tag == "\"release-1\"");
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });
        using var client = new HttpClient(handler);
        var store = new MemoryUpdatePreferenceStore(UpdatePreference.Default);
        using var service = CreateService(directory.Path, store, client);

        var first = await service.CheckAsync(force: true, TestContext.Current.CancellationToken);
        var second = await service.CheckAsync(force: true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateAvailability.Available, first.Availability);
        Assert.Equal(UpdateAvailability.Available, second.Availability);
        Assert.Equal(2, call);
    }

    [Fact]
    public async Task AutomaticCheckSkipsWhenDisabledOrStillInsideCadence()
    {
        using var directory = TemporaryDirectory.Create();
        using var handler = new QueueMessageHandler(_ => JsonResponse(CreateReleaseJson()));
        using var client = new HttpClient(handler);
        using var disabledService = CreateService(
            directory.Path,
            new MemoryUpdatePreferenceStore(new UpdatePreference(false, null, null)),
            client);

        var disabled = await disabledService.CheckAsync(force: false, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateAvailability.NotChecked, disabled.Availability);
        Assert.Equal(0, handler.CallCount);

        using var cadenceService = CreateService(
            directory.Path,
            new MemoryUpdatePreferenceStore(new UpdatePreference(true, DateTimeOffset.UtcNow, null)),
            client);
        var insideCadence = await cadenceService.CheckAsync(force: false, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateAvailability.NotChecked, insideCadence.Availability);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CheckConvertsNetworkFailureToNonBlockingResult()
    {
        using var directory = TemporaryDirectory.Create();
        using var handler = new QueueMessageHandler(_ => throw new HttpRequestException("offline"));
        using var client = new HttpClient(handler);
        using var service = CreateService(directory.Path, new MemoryUpdatePreferenceStore(UpdatePreference.Default), client);

        var result = await service.CheckAsync(force: true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateAvailability.Unavailable, result.Availability);
        Assert.Contains("游戏库", result.Message, StringComparison.Ordinal);
    }

    private static GitHubApplicationUpdateService CreateService(
        string root,
        IUpdatePreferenceStore store,
        HttpClient client)
    {
        var paths = GameNestDataPaths.CreateForRoot(root);
        var options = new ApplicationUpdateOptions(
            "wangjingping-88",
            "GameNest",
            "2026-03-10",
            "win-x64",
            new Version(0, 2, 0),
            root,
            TimeSpan.FromHours(24),
            1024 * 1024,
            []);
        return new GitHubApplicationUpdateService(
            paths,
            store,
            new StubMaintenanceService(),
            options,
            client,
            NullLogger<GitHubApplicationUpdateService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static string CreateReleaseJson(bool prerelease = false, bool includeSignature = true)
    {
        const string baseName = "GameNest-0.2.1-win-x64-portable";
        var assets = new List<object>
        {
            new
            {
                name = baseName + ".zip",
                browser_download_url = "https://github.com/wangjingping-88/GameNest/releases/download/v0.2.1/" + baseName + ".zip",
                size = 4096,
                digest = "sha256:" + new string('A', 64),
            },
            new
            {
                name = baseName + ".update.json",
                browser_download_url = "https://github.com/wangjingping-88/GameNest/releases/download/v0.2.1/" + baseName + ".update.json",
                size = 512,
                digest = (string?)null,
            },
        };
        if (includeSignature)
        {
            assets.Add(new
            {
                name = baseName + ".update.sig",
                browser_download_url = "https://github.com/wangjingping-88/GameNest/releases/download/v0.2.1/" + baseName + ".update.sig",
                size = 64,
                digest = (string?)null,
            });
        }

        return JsonSerializer.Serialize(new
        {
            tag_name = "v0.2.1",
            name = "GameNest 0.2.1",
            body = "修复与改进",
            html_url = "https://github.com/wangjingping-88/GameNest/releases/tag/v0.2.1",
            published_at = new DateTimeOffset(2026, 8, 6, 1, 2, 3, TimeSpan.Zero),
            draft = false,
            prerelease,
            assets,
        });
    }

    private sealed class QueueMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            CallCount++;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class MemoryUpdatePreferenceStore(UpdatePreference preference) : IUpdatePreferenceStore
    {
        private UpdatePreference _preference = preference;

        public Task<UpdatePreference> GetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_preference);
        }

        public Task SetAsync(UpdatePreference value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _preference = value;
            return Task.CompletedTask;
        }
    }

    private sealed class StubMaintenanceService : IApplicationMaintenanceService
    {
        public Task<DataBackupResult> CreateAutomaticBackupAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DataBackupResult> CreateManualBackupAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CacheCleanupResult> CleanupImageCacheAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DiagnosticsExportResult> ExportDiagnosticsAsync(
            string destinationDirectory,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
