using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GameNest.Application;

public static class GameCandidateFingerprint
{
    public static string Create(string path, long fileSize, DateTimeOffset lastWriteUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);

        var value = string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFullPath(path).ToUpperInvariant()}|{fileSize}|{lastWriteUtc.UtcTicks}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
