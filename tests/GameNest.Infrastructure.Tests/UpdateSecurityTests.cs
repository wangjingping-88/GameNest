using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GameNest.Application;
using GameNest.Infrastructure.Updates;

namespace GameNest.Infrastructure.Tests;

public sealed class UpdateSecurityTests
{
    [Fact]
    public void ManifestVerifierAcceptsTrustedP256Signature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateManifestFixture(key, packageSize: 128);

        var actual = fixture.Verifier.Verify(fixture.ManifestBytes, fixture.Signature, fixture.Release);

        Assert.Equal("0.2.1", actual.Version);
        Assert.Equal(fixture.Release.Package.Name, actual.AssetName);
    }

    [Fact]
    public void ManifestVerifierRejectsWrongSignature()
    {
        using var trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateManifestFixture(trustedKey, packageSize: 128);
        var forgedSignature = attackerKey.SignData(
            fixture.ManifestBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Assert.Throws<InvalidDataException>(() =>
            fixture.Verifier.Verify(fixture.ManifestBytes, forgedSignature, fixture.Release));
    }

    [Fact]
    public void ManifestVerifierRejectsOversizedPackage()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateManifestFixture(key, packageSize: 2048, maximumPackageBytes: 1024);

        Assert.Throws<InvalidDataException>(() =>
            fixture.Verifier.Verify(fixture.ManifestBytes, fixture.Signature, fixture.Release));
    }

    [Fact]
    public void PackageIntegrityVerifierRejectsWrongHash()
    {
        var manifest = new UpdatePackageManifest(
            1,
            "0.2.1",
            "stable",
            "win-x64",
            "GameNest-0.2.1-win-x64-portable.zip",
            128,
            new string('A', 64),
            19041,
            new DateTimeOffset(2026, 8, 6, 1, 2, 3, TimeSpan.Zero),
            "test-2026");

        Assert.Throws<InvalidDataException>(() =>
            UpdatePackageIntegrityVerifier.Verify(128, new string('B', 64), manifest, 1024));
    }

    [Fact]
    public async Task ArchiveExtractorAcceptsPortableRoot()
    {
        using var directory = TemporaryDirectory.Create();
        var archiveFile = Path.Combine(directory.Path, "valid.zip");
        CreateArchive(archiveFile, new Dictionary<string, byte[]>
        {
            [".gamenest-portable-root"] = [1],
            ["GameNest.App.exe"] = [2],
            ["VERSION.txt"] = [3],
            ["Assets/cover.png"] = [4],
        });
        var destination = Path.Combine(directory.Path, "destination");

        await SafeUpdateArchiveExtractor.ExtractAsync(
            archiveFile,
            destination,
            1024,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(destination, "Assets", "cover.png")));
    }

    [Fact]
    public async Task ArchiveExtractorRejectsPathTraversal()
    {
        using var directory = TemporaryDirectory.Create();
        var archiveFile = Path.Combine(directory.Path, "malicious.zip");
        CreateArchive(archiveFile, new Dictionary<string, byte[]>
        {
            [".gamenest-portable-root"] = [1],
            ["GameNest.App.exe"] = [2],
            ["VERSION.txt"] = [3],
            ["../escaped.txt"] = [4],
        });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SafeUpdateArchiveExtractor.ExtractAsync(
                archiveFile,
                Path.Combine(directory.Path, "destination"),
                1024,
                TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(directory.Path, "escaped.txt")));
    }

    [Fact]
    public async Task ArchiveExtractorRejectsExpandedSizeOverLimit()
    {
        using var directory = TemporaryDirectory.Create();
        var archiveFile = Path.Combine(directory.Path, "large.zip");
        CreateArchive(archiveFile, new Dictionary<string, byte[]>
        {
            [".gamenest-portable-root"] = [1],
            ["GameNest.App.exe"] = new byte[2048],
            ["VERSION.txt"] = [3],
        });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SafeUpdateArchiveExtractor.ExtractAsync(
                archiveFile,
                Path.Combine(directory.Path, "destination"),
                1024,
                TestContext.Current.CancellationToken));
    }

    private static ManifestFixture CreateManifestFixture(
        ECDsa key,
        long packageSize,
        long maximumPackageBytes = 4096)
    {
        const string assetName = "GameNest-0.2.1-win-x64-portable.zip";
        var publishedAt = new DateTimeOffset(2026, 8, 6, 1, 2, 3, TimeSpan.Zero);
        var release = new UpdateRelease(
            new Version(0, 2, 1),
            "v0.2.1",
            "GameNest 0.2.1",
            "notes",
            new Uri("https://github.com/wangjingping-88/GameNest/releases/tag/v0.2.1"),
            publishedAt,
            new UpdateReleaseAsset(assetName, new Uri("https://github.com/package.zip"), packageSize, null),
            new UpdateReleaseAsset(assetName + ".update.json", new Uri("https://github.com/manifest"), 1, null),
            new UpdateReleaseAsset(assetName + ".update.sig", new Uri("https://github.com/signature"), 1, null));
        var manifest = new UpdatePackageManifest(
            1,
            "0.2.1",
            "stable",
            "win-x64",
            assetName,
            packageSize,
            new string('A', 64),
            19041,
            publishedAt,
            "test-2026");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var signature = key.SignData(
            bytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var options = new ApplicationUpdateOptions(
            "wangjingping-88",
            "GameNest",
            "2026-03-10",
            "win-x64",
            new Version(0, 2, 0),
            AppContext.BaseDirectory,
            TimeSpan.FromHours(24),
            maximumPackageBytes,
            [new UpdateTrustedKey("test-2026", Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))]);
        return new ManifestFixture(new UpdateManifestVerifier(options), release, bytes, signature);
    }

    private static void CreateArchive(string archiveFile, IReadOnlyDictionary<string, byte[]> entries)
    {
        using var archive = ZipFile.Open(archiveFile, ZipArchiveMode.Create);
        foreach (var pair in entries)
        {
            var entry = archive.CreateEntry(pair.Key);
            using var stream = entry.Open();
            stream.Write(pair.Value);
        }
    }

    private sealed record ManifestFixture(
        UpdateManifestVerifier Verifier,
        UpdateRelease Release,
        byte[] ManifestBytes,
        byte[] Signature);
}
