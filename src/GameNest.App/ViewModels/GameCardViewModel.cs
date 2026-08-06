using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameNest.Application;
using GameNest.Domain;
using System.Globalization;

namespace GameNest.App.ViewModels;

public sealed partial class GameCardViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteGlyph))]
    [NotifyPropertyChangedFor(nameof(FavoriteLabel))]
    public partial bool IsFavorite { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuntimeStatusText))]
    [NotifyPropertyChangedFor(nameof(RuntimeStatusGlyph))]
    [NotifyPropertyChangedFor(nameof(LaunchButtonText))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(IsSessionActive))]
    public partial GameRuntimeState RuntimeState { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuntimeStatusText))]
    public partial int? ProcessId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessConfidenceText))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    public partial GameProcessConfidence ProcessConfidence { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionDurationText))]
    public partial DateTimeOffset? SessionStartedAtUtc { get; set; }

    public GameCardViewModel(
        Game game,
        Func<GameCardViewModel, CancellationToken, Task>? toggleFavorite = null,
        Func<GameCardViewModel, CancellationToken, Task>? launch = null)
    {
        Model = game;
        IsFavorite = game.IsFavorite;
        RuntimeState = GameRuntimeState.NotRunning;
        ProcessConfidence = GameProcessConfidence.Unconfirmed;
        ToggleFavoriteCommand = new AsyncRelayCommand(
            cancellationToken => toggleFavorite?.Invoke(this, cancellationToken) ?? Task.CompletedTask);
        LaunchCommand = new AsyncRelayCommand(
            cancellationToken => launch?.Invoke(this, cancellationToken) ?? Task.CompletedTask);
    }

    public IAsyncRelayCommand ToggleFavoriteCommand { get; }

    public IAsyncRelayCommand LaunchCommand { get; }

    public Game Model { get; private set; }

    public Guid Id => Model.Id;

    public string Title => Model.Title;

    public string DescriptionText => Model.Description ?? "尚未添加简介";

    public string ExecutablePath => Model.LaunchProfile.ExecutablePath;

    public string WorkingDirectory => Model.LaunchProfile.WorkingDirectory;

    public string ArgumentsText => Model.LaunchProfile.Arguments ?? "无";

    public string? IconPath => Model.Icon?.LocalPath;

    public string? CoverPath => Model.Cover?.LocalPath;

    public bool HasCover => Model.Cover is not null;

    public bool HasNoCover => !HasCover;

    public bool IsCoverManuallyDisabled =>
        Model.UserEditedFields.Contains(GameEditableField.Cover);

    public DateTimeOffset DateAddedUtc => Model.DateAddedUtc;

    public DateTimeOffset? LastPlayedUtc => Model.LastPlayedUtc;

    public string LastPlayedText => Model.LastPlayedUtc is null
        ? "尚未启动"
        : Model.LastPlayedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    public string CardSubtitleText => Model.LastPlayedUtc is null ? SourceText : $"最近游玩 · {LastPlayedText}";

    public string SourceText => Model.SourceType switch
    {
        GameSourceType.ManualShortcut => "手工快捷方式",
        GameSourceType.ManualExecutable => "手工 EXE",
        GameSourceType.Steam => "Steam 本地清单",
        GameSourceType.DiscoveredShortcut => "扫描到的快捷方式",
        GameSourceType.DiscoveredExecutable => "扫描到的 EXE",
        _ => "本地来源",
    };

    public string MetadataSourceText => Model.MetadataAttribution is null
        ? "本地手工信息"
        : $"元数据：{Model.MetadataAttribution.SourceName}";

    public string CoverDescription => HasCover
        ? $"{Title} 的本地封面"
        : $"{Title} 尚未设置封面";

    public string TotalPlayTimeText => Model.TotalPlaySeconds < 60
        ? "不足 1 分钟"
        : $"{TimeSpan.FromSeconds(Model.TotalPlaySeconds).TotalHours:F1} 小时";

    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";

    public string FavoriteLabel => IsFavorite ? "取消收藏" : "加入收藏";

    public string RuntimeStatusText => RuntimeState switch
    {
        GameRuntimeState.Launching => "启动中",
        GameRuntimeState.Running => ProcessId is null ? "正在运行" : $"正在运行 · PID {ProcessId}",
        GameRuntimeState.Stopping => "正在请求关闭",
        _ => "未运行",
    };

    public string RuntimeStatusGlyph => RuntimeState switch
    {
        GameRuntimeState.Launching => "\uE895",
        GameRuntimeState.Running => "\uE768",
        GameRuntimeState.Stopping => "\uE7E8",
        _ => "\uEA39",
    };

    public string LaunchButtonText => RuntimeState switch
    {
        GameRuntimeState.Launching => "正在启动",
        GameRuntimeState.Running => "运行中",
        GameRuntimeState.Stopping => "正在停止",
        _ => "启动游戏",
    };

    public string ProcessConfidenceText => ProcessConfidence switch
    {
        GameProcessConfidence.Confirmed => "已确认游戏进程",
        GameProcessConfidence.Probable => "仅跟踪 · 进程未完全确认",
        _ => "等待识别游戏进程",
    };

    public bool CanStop =>
        RuntimeState == GameRuntimeState.Running &&
        ProcessConfidence == GameProcessConfidence.Confirmed;

    public bool IsSessionActive => RuntimeState is
        GameRuntimeState.Launching or GameRuntimeState.Running or GameRuntimeState.Stopping;

    public string SessionDurationText => SessionStartedAtUtc is null
        ? "—"
        : FormatDuration(DateTimeOffset.UtcNow - SessionStartedAtUtc.Value);

    public void Update(Game game)
    {
        if (game.Id != Id)
        {
            throw new ArgumentException("不能用其他游戏更新当前卡片。", nameof(game));
        }

        Model = game;
        IsFavorite = game.IsFavorite;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(ExecutablePath));
        OnPropertyChanged(nameof(WorkingDirectory));
        OnPropertyChanged(nameof(ArgumentsText));
        OnPropertyChanged(nameof(IconPath));
        OnPropertyChanged(nameof(CoverPath));
        OnPropertyChanged(nameof(HasCover));
        OnPropertyChanged(nameof(HasNoCover));
        OnPropertyChanged(nameof(DateAddedUtc));
        OnPropertyChanged(nameof(LastPlayedUtc));
        OnPropertyChanged(nameof(LastPlayedText));
        OnPropertyChanged(nameof(CardSubtitleText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(MetadataSourceText));
        OnPropertyChanged(nameof(CoverDescription));
        OnPropertyChanged(nameof(TotalPlayTimeText));
    }

    public void UpdateRuntime(GameRuntimeSnapshot runtime)
    {
        if (runtime.GameId != Id)
        {
            throw new ArgumentException("运行状态必须属于当前游戏。", nameof(runtime));
        }

        ProcessId = runtime.PrimaryProcessId;
        ProcessConfidence = runtime.Confidence;
        SessionStartedAtUtc = runtime.SessionStartedAtUtc;
        RuntimeState = runtime.State;
        OnPropertyChanged(nameof(SessionDurationText));
    }

    public void RefreshSessionClock() => OnPropertyChanged(nameof(SessionDurationText));

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
}
