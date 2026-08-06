namespace GameNest.Domain;

public sealed record OverlayProfile
{
    private static readonly int[] SupportedScales = [75, 100, 125, 150];

    public OverlayProfile(
        Guid id,
        Guid? gameId,
        bool isEnabled,
        OverlayPosition position,
        int scalePercent,
        int backgroundOpacityPercent,
        bool showFps,
        bool showCpu,
        bool showGpu,
        bool showRam,
        string toggleHotkey,
        bool hideWhenGameNotForeground,
        DateTimeOffset updatedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("覆盖层配置 ID 不能为空。", nameof(id));
        }

        if (!SupportedScales.Contains(scalePercent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scalePercent),
                "覆盖层缩放只支持 75%、100%、125% 或 150%。");
        }

        if (backgroundOpacityPercent is < 50 or > 95)
        {
            throw new ArgumentOutOfRangeException(
                nameof(backgroundOpacityPercent),
                "覆盖层背景不透明度必须在 50% 至 95% 之间。");
        }

        var hotkey = OverlayHotkey.Parse(toggleHotkey);
        Id = id;
        GameId = gameId;
        IsEnabled = isEnabled;
        Position = position;
        ScalePercent = scalePercent;
        BackgroundOpacityPercent = backgroundOpacityPercent;
        ShowFps = showFps;
        ShowCpu = showCpu;
        ShowGpu = showGpu;
        ShowRam = showRam;
        ToggleHotkey = hotkey.DisplayText;
        HideWhenGameNotForeground = hideWhenGameNotForeground;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; }

    public Guid? GameId { get; }

    public bool IsEnabled { get; }

    public OverlayPosition Position { get; }

    public int ScalePercent { get; }

    public int BackgroundOpacityPercent { get; }

    public bool ShowFps { get; }

    public bool ShowCpu { get; }

    public bool ShowGpu { get; }

    public bool ShowRam { get; }

    public string ToggleHotkey { get; }

    public bool HideWhenGameNotForeground { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public static OverlayProfile CreateDefault(Guid? gameId = null, DateTimeOffset? nowUtc = null) =>
        new(
            Guid.NewGuid(),
            gameId,
            true,
            OverlayPosition.TopRight,
            100,
            88,
            true,
            true,
            true,
            true,
            OverlayHotkey.Default.DisplayText,
            true,
            nowUtc ?? DateTimeOffset.UtcNow);

    public static OverlayProfile Resolve(OverlayProfile globalProfile, OverlayProfile? gameProfile)
    {
        ArgumentNullException.ThrowIfNull(globalProfile);
        if (globalProfile.GameId is not null)
        {
            throw new ArgumentException("全局覆盖层配置不能关联游戏。", nameof(globalProfile));
        }

        return gameProfile ?? globalProfile;
    }
}
