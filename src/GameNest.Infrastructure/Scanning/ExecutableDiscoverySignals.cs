using System.Diagnostics;
using System.ComponentModel;
using GameNest.Domain;

namespace GameNest.Infrastructure.Scanning;

internal static class ExecutableDiscoverySignals
{
    public static IReadOnlyList<GameCandidateEvidence> Inspect(
        string executablePath,
        IReadOnlyCollection<string> siblingFiles,
        IReadOnlyCollection<string> childDirectories)
    {
        var evidence = new List<GameCandidateEvidence>();
        if (siblingFiles.Any(static file =>
                Path.GetFileName(file).StartsWith("steam_api", StringComparison.OrdinalIgnoreCase)
                && Path.GetExtension(file).Equals(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            evidence.Add(new("steam-api", "同目录包含 steam_api.dll", 35));
        }

        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        if (siblingFiles.Any(static file =>
                Path.GetFileName(file).Equals("UnityPlayer.dll", StringComparison.OrdinalIgnoreCase))
            || childDirectories.Any(directory =>
                Path.GetFileName(directory).Equals(executableName + "_Data", StringComparison.OrdinalIgnoreCase))
            || childDirectories.Any(directory =>
                Path.GetFileName(directory).Equals("Content", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(directory).Equals("Paks", StringComparison.OrdinalIgnoreCase)))
        {
            evidence.Add(new("engine-layout", "检测到 Unity 或 Unreal 常见目录结构", 25));
        }

        try
        {
            var version = FileVersionInfo.GetVersionInfo(executablePath);
            if (!string.IsNullOrWhiteSpace(version.ProductName)
                || !string.IsNullOrWhiteSpace(version.FileDescription))
            {
                evidence.Add(new("version-metadata", "EXE 包含产品名或文件说明", 15));
            }
        }
        catch (Exception exception) when (exception is FileNotFoundException or Win32Exception)
        {
        }

        if (siblingFiles.Any(static file =>
                Path.GetFileNameWithoutExtension(file).Contains("cover", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(file).Contains("background", StringComparison.OrdinalIgnoreCase)))
        {
            evidence.Add(new("local-artwork", "同目录包含封面或背景图片", 10));
        }

        return evidence;
    }

    public static string GetTitle(string executablePath)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(executablePath);
            return FirstNonEmpty(
                version.ProductName,
                version.FileDescription,
                Path.GetFileNameWithoutExtension(executablePath));
        }
        catch (Exception exception) when (exception is FileNotFoundException or Win32Exception)
        {
            return Path.GetFileNameWithoutExtension(executablePath);
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(static value => !string.IsNullOrWhiteSpace(value))!.Trim();
}
