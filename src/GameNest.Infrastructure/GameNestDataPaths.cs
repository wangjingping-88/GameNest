namespace GameNest.Infrastructure;

public sealed record GameNestDataPaths(
    string RootDirectory,
    string DatabaseFile,
    string LogDirectory,
    string AssetDirectory,
    string BackupDirectory)
{
    public IReadOnlyList<string> ImageCacheDirectories =>
    [
        Path.Combine(AssetDirectory, "cache"),
        Path.Combine(AssetDirectory, "icons"),
    ];

    public string UpdateDirectory => Path.Combine(RootDirectory, "updates");

    public static GameNestDataPaths CreateDefault()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("GAMENEST_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return CreateForRoot(configuredRoot);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return CreateForRoot(Path.Combine(localAppData, "GameNest"));
    }

    public static GameNestDataPaths CreateForRoot(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var fullRoot = Path.GetFullPath(rootDirectory);
        return new GameNestDataPaths(
            fullRoot,
            Path.Combine(fullRoot, "data", "gamenest.db"),
            Path.Combine(fullRoot, "logs"),
            Path.Combine(fullRoot, "assets"),
            Path.Combine(fullRoot, "backups"));
    }
}
