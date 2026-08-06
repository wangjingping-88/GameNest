using System.Security.Cryptography;

namespace GameNest.Infrastructure.Updates;

public static class UpdatePackageIntegrityVerifier
{
    public static void Verify(
        long actualSizeBytes,
        string actualSha256,
        UpdatePackageManifest manifest,
        long maximumPackageBytes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (actualSizeBytes <= 0 ||
            actualSizeBytes != manifest.SizeBytes ||
            actualSizeBytes > maximumPackageBytes)
        {
            throw new InvalidDataException("更新包大小与签名清单或安全限制不一致。");
        }

        byte[] actualHash;
        byte[] expectedHash;
        try
        {
            actualHash = Convert.FromHexString(actualSha256);
            expectedHash = Convert.FromHexString(manifest.Sha256);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("更新包 SHA-256 格式无效。", exception);
        }

        if (actualHash.Length != 32 || expectedHash.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            throw new InvalidDataException("更新包 SHA-256 校验失败，文件已被拒绝。");
        }
    }
}
