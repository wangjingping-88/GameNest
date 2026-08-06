using System.Security.Cryptography;

namespace GameNest.Telemetry;

public sealed record PresentMonOptions(
    string ExecutablePath,
    string Version,
    string Sha256)
{
    public const string SupportedVersion = "2.5.1";
    public const string SupportedSha256 = "9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191";

    public static PresentMonOptions CreateDefault()
    {
        var configured = Environment.GetEnvironmentVariable("GAMENEST_PRESENTMON_PATH");
        var candidates = new[]
        {
            configured,
            Path.Combine(AppContext.BaseDirectory, "Tools", "PresentMon", "PresentMon-2.5.1-x64.exe"),
        };
        var selected = candidates.FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                       ?? candidates[1]!;
        return new PresentMonOptions(selected, SupportedVersion, SupportedSha256);
    }

    public async Task<bool> VerifyHashAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(ExecutablePath))
        {
            return false;
        }

        await using var stream = new FileStream(
            ExecutablePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        return hash.Equals(Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
