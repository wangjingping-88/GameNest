using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameNest.Application;
using Microsoft.Extensions.Logging;

namespace GameNest.Infrastructure.Updates;

public sealed class GitHubApplicationUpdateService : IApplicationUpdateService, IDisposable
{
    private static readonly Action<ILogger, Exception?> UpdateCheckFailed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(6100, nameof(UpdateCheckFailed)),
        "GitHub Release 更新检查未完成。");
    private static readonly Action<ILogger, Exception?> UpdateCheckNetworkFailed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(6101, nameof(UpdateCheckNetworkFailed)),
        "GitHub Release 更新检查网络异常。");
    private static readonly Action<ILogger, Exception?> UpdateResponseInvalid = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(6102, nameof(UpdateResponseInvalid)),
        "GitHub Release 响应格式无效。");
    private static readonly Action<ILogger, Exception?> UpdateCheckTimeSaveFailed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(6103, nameof(UpdateCheckTimeSaveFailed)),
        "无法保存更新检查时间。");
    private const int MaximumManifestBytes = 64 * 1024;
    private const int MaximumSignatureBytes = 1024;
    private const int MaximumRedirects = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GameNestDataPaths _paths;
    private readonly IUpdatePreferenceStore _preferenceStore;
    private readonly IApplicationMaintenanceService _maintenanceService;
    private readonly ApplicationUpdateOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubApplicationUpdateService> _logger;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private bool _disposed;

    public GitHubApplicationUpdateService(
        GameNestDataPaths paths,
        IUpdatePreferenceStore preferenceStore,
        IApplicationMaintenanceService maintenanceService,
        ApplicationUpdateOptions options,
        HttpClient httpClient,
        ILogger<GitHubApplicationUpdateService> logger)
    {
        _paths = paths;
        _preferenceStore = preferenceStore;
        _maintenanceService = maintenanceService;
        _options = options;
        _httpClient = httpClient;
        _logger = logger;
    }

    public Version CurrentVersion => _options.CurrentVersion;

    public Task<UpdatePreference> GetPreferenceAsync(CancellationToken cancellationToken) =>
        _preferenceStore.GetAsync(cancellationToken);

    public async Task SetAutomaticCheckEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        var preference = await _preferenceStore.GetAsync(cancellationToken).ConfigureAwait(false);
        await _preferenceStore
            .SetAsync(preference with { AutomaticCheckEnabled = enabled }, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<UpdateCheckResult> CheckAsync(bool force, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var preference = await _preferenceStore.GetAsync(cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            if (!force && !preference.AutomaticCheckEnabled)
            {
                return CreateResult(UpdateAvailability.NotChecked, null, now, "自动检查更新已关闭。");
            }

            if (!force && preference.LastCheckedUtc is { } lastChecked &&
                now - lastChecked < _options.AutomaticCheckInterval)
            {
                return CreateResult(UpdateAvailability.NotChecked, null, now, "距离上次检查不足 24 小时。");
            }

            try
            {
                var (releaseJson, entityTag) = await FetchLatestReleaseAsync(preference, cancellationToken)
                    .ConfigureAwait(false);
                await _preferenceStore
                    .SetAsync(preference with { LastCheckedUtc = now, EntityTag = entityTag }, cancellationToken)
                    .ConfigureAwait(false);

                if (releaseJson is null)
                {
                    return CreateResult(UpdateAvailability.UpToDate, null, now, "当前没有可用的正式版本。");
                }

                var release = ParseRelease(releaseJson);
                if (release is null || release.Version <= CurrentVersion)
                {
                    return CreateResult(UpdateAvailability.UpToDate, null, now, "当前已是最新正式版本。");
                }

                var capability = await GetInstallCapabilityAsync(cancellationToken).ConfigureAwait(false);
                return CreateResult(
                    UpdateAvailability.Available,
                    release,
                    now,
                    $"发现 GameNest {ApplicationVersion.Format(release.Version)}。",
                    capability);
            }
            catch (GitHubUpdateException exception)
            {
                await SaveCheckTimeBestEffortAsync(preference, now, cancellationToken).ConfigureAwait(false);
                UpdateCheckFailed(_logger, exception);
                return CreateResult(UpdateAvailability.Unavailable, null, now, exception.Message);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await SaveCheckTimeBestEffortAsync(preference, now, CancellationToken.None).ConfigureAwait(false);
                return CreateResult(UpdateAvailability.Unavailable, null, now, "检查更新超时，请稍后重试。");
            }
            catch (HttpRequestException exception)
            {
                await SaveCheckTimeBestEffortAsync(preference, now, cancellationToken).ConfigureAwait(false);
                UpdateCheckNetworkFailed(_logger, exception);
                return CreateResult(UpdateAvailability.Unavailable, null, now, "当前无法连接 GitHub，游戏库仍可离线使用。");
            }
            catch (JsonException exception)
            {
                await SaveCheckTimeBestEffortAsync(preference, now, cancellationToken).ConfigureAwait(false);
                UpdateResponseInvalid(_logger, exception);
                return CreateResult(UpdateAvailability.Unavailable, null, now, "GitHub 返回了无法识别的版本信息。");
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task<PreparedApplicationUpdate> PrepareAsync(
        UpdateRelease release,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_options.TrustedKeys.Count == 0)
        {
            throw new InvalidOperationException("当前版本尚未内置生产更新公钥，只能打开 GitHub 下载页手动更新。");
        }

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var operationId = Guid.NewGuid().ToString("N");
            var operationRoot = Path.Combine(_paths.UpdateDirectory, "staging", operationId);
            var extractedRoot = Path.Combine(operationRoot, "package");
            var downloadDirectory = Path.Combine(_paths.UpdateDirectory, "downloads", release.TagName);
            var packageFile = Path.Combine(downloadDirectory, release.Package.Name);
            await CreateDirectoriesAsync([operationRoot, downloadDirectory], cancellationToken).ConfigureAwait(false);

            progress?.Report(new UpdateProgress(UpdateOperationStage.DownloadingManifest, 0, null, "正在下载签名清单…"));
            var manifestBytes = await DownloadBytesAsync(
                release.Manifest.DownloadUri,
                MaximumManifestBytes,
                cancellationToken).ConfigureAwait(false);
            var signatureBytes = await DownloadBytesAsync(
                release.Signature.DownloadUri,
                MaximumSignatureBytes,
                cancellationToken).ConfigureAwait(false);

            progress?.Report(new UpdateProgress(UpdateOperationStage.VerifyingManifest, 0, null, "正在验证更新发布者…"));
            var manifest = new UpdateManifestVerifier(_options).Verify(manifestBytes, signatureBytes, release);
            if (OperatingSystem.IsWindows() && Environment.OSVersion.Version.Build < manifest.MinimumOsBuild)
            {
                throw new InvalidOperationException($"此更新要求 Windows Build {manifest.MinimumOsBuild} 或更高版本。");
            }

            progress?.Report(new UpdateProgress(UpdateOperationStage.DownloadingPackage, 0, manifest.SizeBytes, "正在下载更新包…"));
            var packageHash = await DownloadPackageAsync(
                release.Package.DownloadUri,
                packageFile,
                manifest.SizeBytes,
                progress,
                cancellationToken).ConfigureAwait(false);
            UpdatePackageIntegrityVerifier.Verify(
                manifest.SizeBytes,
                packageHash,
                manifest,
                _options.MaximumPackageBytes);

            progress?.Report(new UpdateProgress(UpdateOperationStage.ExtractingPackage, 0, null, "正在安全解压更新包…"));
            await SafeUpdateArchiveExtractor.ExtractAsync(
                packageFile,
                extractedRoot,
                checked(_options.MaximumPackageBytes * 3),
                cancellationToken).ConfigureAwait(false);
            await ValidateExtractedVersionAsync(extractedRoot, release.Version, cancellationToken).ConfigureAwait(false);

            progress?.Report(new UpdateProgress(UpdateOperationStage.PreparingInstaller, 0, null, "正在备份数据库并准备升级…"));
            var backup = await _maintenanceService.CreateManualBackupAsync(cancellationToken).ConfigureAwait(false);
            var installRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_options.InstallRoot));
            var parent = Path.GetDirectoryName(installRoot)
                         ?? throw new InvalidOperationException("无法确定便携版安装目录的父目录。");
            if (!File.Exists(Path.Combine(installRoot, ".gamenest-portable-root")))
            {
                throw new InvalidOperationException("当前目录不是可验证的 GameNest 便携版目录，不能自动替换。");
            }

            var candidateRoot = Path.Combine(parent, $".GameNest.update-{operationId}");
            var rollbackRoot = Path.Combine(parent, $".GameNest.rollback-{operationId}");
            await CopyDirectoryAsync(extractedRoot, candidateRoot, cancellationToken).ConfigureAwait(false);
            var planDirectory = Path.Combine(_paths.UpdateDirectory, "plans");
            var planFile = Path.Combine(planDirectory, $"{operationId}.json");
            var plan = new PortableUpdatePlan(
                1,
                0,
                installRoot,
                candidateRoot,
                rollbackRoot,
                operationRoot,
                Path.Combine(operationRoot, "health.ok"),
                Path.Combine(operationRoot, "health.failed"),
                _paths.DatabaseFile,
                backup.BackupFile,
                ApplicationVersion.Format(release.Version));
            await PortableUpdatePlanStore.WriteAsync(planFile, plan, cancellationToken).ConfigureAwait(false);

            progress?.Report(new UpdateProgress(UpdateOperationStage.ReadyToInstall, manifest.SizeBytes, manifest.SizeBytes, "更新已验证，可以安装。"));
            return new PreparedApplicationUpdate(
                release,
                Path.Combine(extractedRoot, "GameNest.App.exe"),
                planFile,
                operationRoot,
                backup.BackupFile);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException("普通权限无法写入便携版目录，请改为从 GitHub 下载页手动更新。", exception);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task<UpdateLaunchResult> LaunchInstallerAsync(
        PreparedApplicationUpdate preparedUpdate,
        int currentProcessId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparedUpdate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(currentProcessId);

        var plan = await PortableUpdatePlanStore.ReadAsync(preparedUpdate.PlanFile, cancellationToken).ConfigureAwait(false);
        await PortableUpdatePlanStore.WriteAsync(
            preparedUpdate.PlanFile,
            plan with { CurrentProcessId = currentProcessId },
            cancellationToken).ConfigureAwait(false);

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = preparedUpdate.InstallerExecutable,
            Arguments = $"--apply-update \"{preparedUpdate.PlanFile}\"",
            WorkingDirectory = Path.GetDirectoryName(preparedUpdate.InstallerExecutable),
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        return process is null
            ? new UpdateLaunchResult(false, "无法启动暂存升级进程。")
            : new UpdateLaunchResult(true, "升级进程已启动，GameNest 将正常退出。");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _requestGate.Dispose();
        _disposed = true;
    }

    private UpdateCheckResult CreateResult(
        UpdateAvailability availability,
        UpdateRelease? release,
        DateTimeOffset checkedAtUtc,
        string message,
        UpdateInstallCapability? installCapability = null)
    {
        var capability = installCapability ?? GetInstallCapability();
        return new UpdateCheckResult(availability, CurrentVersion, release, capability, checkedAtUtc, message);
    }

    private async Task<UpdateInstallCapability> GetInstallCapabilityAsync(CancellationToken cancellationToken)
    {
        var capability = GetInstallCapability();
        if (capability != UpdateInstallCapability.Ready)
        {
            return capability;
        }

        return await PortableInstallWriteProbe.CanWriteAsync(_options.InstallRoot, cancellationToken)
            .ConfigureAwait(false)
            ? UpdateInstallCapability.Ready
            : UpdateInstallCapability.ProgramDirectoryNotWritable;
    }

    private UpdateInstallCapability GetInstallCapability()
    {
        if (!OperatingSystem.IsWindows() || Environment.OSVersion.Version.Build < 19041)
        {
            return UpdateInstallCapability.UnsupportedPlatform;
        }

        if (_options.TrustedKeys.Count == 0)
        {
            return UpdateInstallCapability.TrustedSigningKeyUnavailable;
        }

        return File.Exists(Path.Combine(_options.InstallRoot, ".gamenest-portable-root"))
            ? UpdateInstallCapability.Ready
            : UpdateInstallCapability.NotPortable;
    }

    private async Task<(byte[]? Json, string? EntityTag)> FetchLatestReleaseAsync(
        UpdatePreference preference,
        CancellationToken cancellationToken)
    {
        var cacheFile = Path.Combine(_paths.UpdateDirectory, "release-cache.json");
        using var request = CreateApiRequest();
        if (!string.IsNullOrWhiteSpace(preference.EntityTag) && File.Exists(cacheFile))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", preference.EntityTag);
        }

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return (await File.ReadAllBytesAsync(cacheFile, cancellationToken).ConfigureAwait(false), preference.EntityTag);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, null);
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new GitHubUpdateException("GitHub API 当前受到限流，请稍后手动重试。");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubUpdateException($"GitHub 更新检查失败（HTTP {(int)response.StatusCode}）。");
        }

        var length = response.Content.Headers.ContentLength;
        if (length is > MaximumManifestBytes)
        {
            throw new GitHubUpdateException("GitHub Release 响应超过安全大小限制。");
        }

        var bytes = await ReadLimitedBytesAsync(response.Content, MaximumManifestBytes, cancellationToken)
            .ConfigureAwait(false);
        await Task.Run(() => Directory.CreateDirectory(_paths.UpdateDirectory), cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(cacheFile, bytes, cancellationToken).ConfigureAwait(false);
        return (bytes, response.Headers.ETag?.ToString());
    }

    private HttpRequestMessage CreateApiRequest()
    {
        var uri = new Uri($"https://api.github.com/repos/{_options.RepositoryOwner}/{_options.RepositoryName}/releases/latest");
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd($"GameNest/{ApplicationVersion.Format(CurrentVersion)}");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", _options.GitHubApiVersion);
        return request;
    }

    private UpdateRelease? ParseRelease(ReadOnlySpan<byte> json)
    {
        var githubRelease = JsonSerializer.Deserialize<GitHubReleaseResponse>(json, JsonOptions)
                            ?? throw new JsonException("GitHub Release 响应为空。");
        if (githubRelease.Draft || githubRelease.Prerelease ||
            !ApplicationVersion.TryParseStable(githubRelease.TagName, out var version) ||
            !string.Equals(githubRelease.TagName, $"v{ApplicationVersion.Format(version)}", StringComparison.Ordinal))
        {
            return null;
        }

        var baseName = $"GameNest-{ApplicationVersion.Format(version)}-win-x64-portable";
        var package = FindAsset(githubRelease.Assets, $"{baseName}.zip");
        var manifest = FindAsset(githubRelease.Assets, $"{baseName}.update.json");
        var signature = FindAsset(githubRelease.Assets, $"{baseName}.update.sig");
        if (package is null || manifest is null || signature is null)
        {
            throw new GitHubUpdateException("最新 Release 缺少固定命名的更新资产。");
        }

        if (package.Size <= 0 || package.Size > _options.MaximumPackageBytes)
        {
            throw new GitHubUpdateException("最新 Release 的更新包大小不符合安全限制。");
        }

        if (!Uri.TryCreate(githubRelease.HtmlUrl, UriKind.Absolute, out var pageUri) || !IsAllowedDownloadUri(pageUri))
        {
            throw new GitHubUpdateException("最新 Release 的页面地址不可信。");
        }

        return new UpdateRelease(
            version,
            githubRelease.TagName,
            string.IsNullOrWhiteSpace(githubRelease.Name) ? githubRelease.TagName : githubRelease.Name,
            githubRelease.Body ?? string.Empty,
            pageUri,
            githubRelease.PublishedAt,
            ToAsset(package),
            ToAsset(manifest),
            ToAsset(signature));
    }

    private static GitHubAssetResponse? FindAsset(IEnumerable<GitHubAssetResponse> assets, string name) =>
        assets.SingleOrDefault(asset => string.Equals(asset.Name, name, StringComparison.Ordinal));

    private static UpdateReleaseAsset ToAsset(GitHubAssetResponse asset)
    {
        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var uri) || !IsAllowedDownloadUri(uri))
        {
            throw new GitHubUpdateException($"Release 资产 {asset.Name} 的下载地址不可信。");
        }

        return new UpdateReleaseAsset(asset.Name, uri, asset.Size, asset.Digest);
    }

    private async Task<byte[]> DownloadBytesAsync(Uri uri, int maximumBytes, CancellationToken cancellationToken)
    {
        using var response = await SendDownloadAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"下载更新资产失败（HTTP {(int)response.StatusCode}）。");
        }

        return await ReadLimitedBytesAsync(response.Content, maximumBytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> DownloadPackageAsync(
        Uri uri,
        string destinationFile,
        long expectedBytes,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await SendDownloadAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0 and var contentLength && contentLength != expectedBytes)
        {
            throw new InvalidDataException("更新包 Content-Length 与签名清单不一致。");
        }

        var partialFile = destinationFile + ".partial";
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(
            partialFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long completed = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            completed = checked(completed + read);
            if (completed > expectedBytes || completed > _options.MaximumPackageBytes)
            {
                throw new InvalidDataException("更新包超过签名清单或安全大小限制。");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            progress?.Report(new UpdateProgress(UpdateOperationStage.DownloadingPackage, completed, expectedBytes, "正在下载更新包…"));
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (completed != expectedBytes)
        {
            throw new InvalidDataException("更新包大小与签名清单不一致。");
        }

        output.Close();
        await Task.Run(() => File.Move(partialFile, destinationFile, overwrite: true), cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private async Task<HttpResponseMessage> SendDownloadAsync(Uri initialUri, CancellationToken cancellationToken)
    {
        var uri = initialUri;
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            if (!IsAllowedDownloadUri(uri))
            {
                throw new InvalidDataException("更新资产下载地址不在允许的 GitHub HTTPS 域名中。");
            }

            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd($"GameNest/{ApplicationVersion.Format(CurrentVersion)}");
            var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or
                HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null)
                {
                    throw new HttpRequestException("GitHub 下载重定向缺少目标地址。");
                }

                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }

            return response;
        }

        throw new HttpRequestException("GitHub 下载重定向次数超过限制。");
    }

    private static bool IsAllowedDownloadUri(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.IdnHost;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadLimitedBytesAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException("下载内容超过安全大小限制。");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private static async Task CreateDirectoriesAsync(
        IEnumerable<string> directories,
        CancellationToken cancellationToken) =>
        await Task.Run(() =>
        {
            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(directory);
            }
        }, cancellationToken).ConfigureAwait(false);

    private static Task CopyDirectoryAsync(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            if (Directory.Exists(destinationDirectory))
            {
                throw new IOException("升级候选目录已存在。");
            }

            Directory.CreateDirectory(destinationDirectory);
            foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
            }

            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(file, Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file)), overwrite: false);
            }
        }, cancellationToken);

    private static async Task ValidateExtractedVersionAsync(
        string extractedRoot,
        Version expectedVersion,
        CancellationToken cancellationToken)
    {
        var versionText = await File.ReadAllTextAsync(
            Path.Combine(extractedRoot, "VERSION.txt"),
            cancellationToken).ConfigureAwait(false);
        if (!versionText.Contains($"GameNest {ApplicationVersion.Format(expectedVersion)}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("更新包内 VERSION.txt 与 Release 版本不一致。");
        }
    }

    private async Task SaveCheckTimeBestEffortAsync(
        UpdatePreference preference,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            await _preferenceStore
                .SetAsync(preference with { LastCheckedUtc = checkedAt }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            UpdateCheckTimeSaveFailed(_logger, exception);
        }
    }

    private sealed class GitHubUpdateException(string message) : Exception(message);

    private sealed record GitHubReleaseResponse(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAssetResponse> Assets);

    private sealed record GitHubAssetResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest);
}
