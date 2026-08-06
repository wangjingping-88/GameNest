namespace GameNest.Infrastructure.Scanning;

public interface IShortcutSourceLocator
{
    Task<IReadOnlyList<string>> FindAsync(CancellationToken cancellationToken);
}

public sealed class WindowsShortcutSourceLocator : IShortcutSourceLocator
{
    public Task<IReadOnlyList<string>> FindAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(EnumerateShortcutPaths, cancellationToken);

    private static string[] EnumerateShortcutPaths()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        };
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(static path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)))
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                try
                {
                    foreach (var file in Directory.GetFiles(directory, "*.lnk", SearchOption.TopDirectoryOnly))
                    {
                        result.Add(file);
                    }

                    foreach (var child in Directory.GetDirectories(directory))
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(child);
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return result.ToArray();
    }
}
