using System.Globalization;

namespace GameNest.Application;

public static class ApplicationVersion
{
    public static bool TryParseStable(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith('v'))
        {
            candidate = candidate[1..];
        }

        var metadataIndex = candidate.IndexOfAny(['+', '-']);
        if (metadataIndex >= 0)
        {
            candidate = candidate[..metadataIndex];
        }

        var components = candidate.Split('.', StringSplitOptions.TrimEntries);
        if (components.Length != 3 || components.Any(static item =>
                !int.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return false;
        }

        if (!Version.TryParse(candidate, out var parsed) || parsed.Major < 0 || parsed.Minor < 0 || parsed.Build < 0)
        {
            return false;
        }

        version = parsed;
        return true;
    }

    public static string Format(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }
}
