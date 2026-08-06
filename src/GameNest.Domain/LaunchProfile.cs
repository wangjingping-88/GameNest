namespace GameNest.Domain;

public sealed record LaunchProfile
{
    public LaunchProfile(
        Guid id,
        Guid gameId,
        string name,
        LaunchKind launchKind,
        string executablePath,
        string? arguments,
        string workingDirectory,
        bool runAsAdministrator,
        bool isDefault,
        IEnumerable<string>? expectedProcessNames = null,
        int gracefulStopTimeoutSeconds = 10)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("启动配置 ID 不能为空。", nameof(id));
        }

        if (gameId == Guid.Empty)
        {
            throw new ArgumentException("游戏 ID 不能为空。", nameof(gameId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (gracefulStopTimeoutSeconds is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gracefulStopTimeoutSeconds),
                "正常关闭等待时间必须在 1 到 120 秒之间。");
        }

        Id = id;
        GameId = gameId;
        Name = name.Trim();
        LaunchKind = launchKind;
        ExecutablePath = executablePath;
        Arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();
        WorkingDirectory = workingDirectory;
        RunAsAdministrator = runAsAdministrator;
        IsDefault = isDefault;
        ExpectedProcessNames = (expectedProcessNames ?? [])
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => Path.GetFileNameWithoutExtension(name.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        GracefulStopTimeoutSeconds = gracefulStopTimeoutSeconds;
    }

    public Guid Id { get; }

    public Guid GameId { get; }

    public string Name { get; }

    public LaunchKind LaunchKind { get; }

    public string ExecutablePath { get; }

    public string? Arguments { get; }

    public string WorkingDirectory { get; }

    public bool RunAsAdministrator { get; }

    public bool IsDefault { get; }

    public IReadOnlyList<string> ExpectedProcessNames { get; }

    public int GracefulStopTimeoutSeconds { get; }
}
