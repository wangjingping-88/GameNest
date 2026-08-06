using System.Reflection;
using GameNest.Application;

namespace GameNest.Infrastructure.Updates;

public sealed record UpdateTrustedKey(
    string KeyId,
    string SubjectPublicKeyInfoBase64);

public sealed record ApplicationUpdateOptions(
    string RepositoryOwner,
    string RepositoryName,
    string GitHubApiVersion,
    string RuntimeIdentifier,
    Version CurrentVersion,
    string InstallRoot,
    TimeSpan AutomaticCheckInterval,
    long MaximumPackageBytes,
    IReadOnlyList<UpdateTrustedKey> TrustedKeys)
{
    public const string DefaultRepositoryOwner = "wangjingping-88";
    public const string DefaultRepositoryName = "GameNest";
    public const string DefaultGitHubApiVersion = "2026-03-10";
    public const string DefaultRuntimeIdentifier = "win-x64";
    public const long DefaultMaximumPackageBytes = 1024L * 1024L * 1024L;
    public const string ProductionUpdateKeyId = "GAMENESTPUBLIC";
    public const string ProductionUpdatePublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEMqfR1EWPTKtD0OwOxGhwE1WlpJbN1opxHCKjEFogVvnt6lfrTRNSvs+Hl7hMTzKG2POMtCTQYgf+4lm+rYQZXQ==";

    public static ApplicationUpdateOptions CreateDefault()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var informationalVersion = entryAssembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var fallbackVersion = entryAssembly?.GetName().Version?.ToString(3);
        var selectedVersion = ApplicationVersion.TryParseStable(informationalVersion, out var parsed)
            ? parsed
            : ApplicationVersion.TryParseStable(fallbackVersion, out parsed)
                ? parsed
                : new Version(0, 2, 0);

        return new ApplicationUpdateOptions(
            DefaultRepositoryOwner,
            DefaultRepositoryName,
            DefaultGitHubApiVersion,
            DefaultRuntimeIdentifier,
            selectedVersion,
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
            TimeSpan.FromHours(24),
            DefaultMaximumPackageBytes,
            [new UpdateTrustedKey(ProductionUpdateKeyId, ProductionUpdatePublicKey)]);
    }
}
