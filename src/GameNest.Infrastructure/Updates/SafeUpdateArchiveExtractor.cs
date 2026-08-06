using System.IO.Compression;

namespace GameNest.Infrastructure.Updates;

public static class SafeUpdateArchiveExtractor
{
    private const int MaximumEntryCount = 20_000;

    public static Task ExtractAsync(
        string archiveFile,
        string destinationDirectory,
        long maximumExpandedBytes,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => ExtractCore(archiveFile, destinationDirectory, maximumExpandedBytes, cancellationToken),
            cancellationToken);

    private static void ExtractCore(
        string archiveFile,
        string destinationDirectory,
        long maximumExpandedBytes,
        CancellationToken cancellationToken)
    {
        var destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));
        var destinationPrefix = destinationRoot + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destinationRoot);

        using var archive = ZipFile.OpenRead(archiveFile);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumEntryCount)
        {
            throw new InvalidDataException("更新包条目数量不符合安全限制。");
        }

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.FullName) || Path.IsPathRooted(entry.FullName))
            {
                throw new InvalidDataException("更新包包含无效路径。");
            }

            checked
            {
                expandedBytes += entry.Length;
            }
            if (expandedBytes > maximumExpandedBytes)
            {
                throw new InvalidDataException("更新包解压后大小超出安全限制。");
            }

            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destinationPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新包包含路径穿越条目。");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
                                      ?? throw new InvalidDataException("无法确定更新文件目录。"));
            entry.ExtractToFile(destinationPath, overwrite: false);
        }

        foreach (var requiredFile in new[] { ".gamenest-portable-root", "GameNest.App.exe", "VERSION.txt" })
        {
            if (!File.Exists(Path.Combine(destinationRoot, requiredFile)))
            {
                throw new InvalidDataException($"更新包缺少必需文件：{requiredFile}。");
            }
        }
    }
}
