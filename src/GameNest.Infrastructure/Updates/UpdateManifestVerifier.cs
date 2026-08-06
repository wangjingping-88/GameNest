using System.Security.Cryptography;
using System.Text.Json;
using GameNest.Application;

namespace GameNest.Infrastructure.Updates;

public sealed class UpdateManifestVerifier(ApplicationUpdateOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
    };

    public UpdatePackageManifest Verify(
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signatureBytes,
        UpdateRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (manifestBytes.IsEmpty || signatureBytes.IsEmpty)
        {
            throw new InvalidDataException("更新清单或签名为空。");
        }

        UpdatePackageManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdatePackageManifest>(manifestBytes, JsonOptions)
                       ?? throw new InvalidDataException("更新清单为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("更新清单不是有效 JSON。", exception);
        }

        var trustedKey = options.TrustedKeys.FirstOrDefault(
            key => string.Equals(key.KeyId, manifest.KeyId, StringComparison.Ordinal));
        if (trustedKey is null)
        {
            throw new InvalidDataException($"更新清单使用了不受信任的签名密钥：{manifest.KeyId}。");
        }

        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(trustedKey.SubjectPublicKeyInfoBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("内置更新公钥格式无效。", exception);
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKey, out var consumed);
        if (consumed != publicKey.Length || !ecdsa.VerifyData(
                manifestBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            throw new InvalidDataException("更新清单签名验证失败。");
        }

        ValidateManifest(manifest, release);
        return manifest;
    }

    private void ValidateManifest(UpdatePackageManifest manifest, UpdateRelease release)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidDataException($"不支持更新清单版本 {manifest.SchemaVersion}。");
        }

        if (!ApplicationVersion.TryParseStable(manifest.Version, out var version) || version != release.Version)
        {
            throw new InvalidDataException("更新清单版本与 GitHub Release 不一致。");
        }

        if (!string.Equals(manifest.Channel, "stable", StringComparison.Ordinal) ||
            !string.Equals(manifest.RuntimeIdentifier, options.RuntimeIdentifier, StringComparison.Ordinal) ||
            !string.Equals(manifest.AssetName, release.Package.Name, StringComparison.Ordinal))
        {
            throw new InvalidDataException("更新清单的通道、运行时或资产名称不匹配。");
        }

        if (manifest.SizeBytes <= 0 || manifest.SizeBytes != release.Package.SizeBytes ||
            manifest.SizeBytes > options.MaximumPackageBytes)
        {
            throw new InvalidDataException("更新包大小不符合安全限制。");
        }

        if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("更新清单的 SHA-256 无效。");
        }

        if (manifest.MinimumOsBuild < 19041 ||
            (manifest.PublishedAtUtc - release.PublishedAtUtc).Duration() > TimeSpan.FromMinutes(10))
        {
            throw new InvalidDataException("更新清单的系统版本或发布时间不匹配。");
        }
    }
}
