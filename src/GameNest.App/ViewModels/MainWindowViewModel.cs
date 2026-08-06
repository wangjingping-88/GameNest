using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameNest.Application;
using GameNest.Domain;
using Microsoft.Extensions.Logging;

namespace GameNest.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly Action<ILogger, Exception?> ApplicationInitializationFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2100, nameof(ApplicationInitializationFailed)),
            "应用初始化失败。");

    private static readonly Action<ILogger, Exception?> ThemePreferenceSaveFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2101, nameof(ThemePreferenceSaveFailed)),
            "保存主题设置失败。");

    private static readonly Action<ILogger, string, Exception?> GameOperationFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2102, nameof(GameOperationFailed)),
            "游戏库操作失败：{OperationName}。");

    private readonly IApplicationDataInitializer _dataInitializer;
    private readonly IThemePreferenceStore _themePreferenceStore;
    private readonly GameLibraryService _gameLibraryService;
    private readonly OverlaySettingsService _overlaySettingsService;
    private readonly IOverlayRuntimeCoordinator _overlayRuntimeCoordinator;
    private readonly IApplicationMaintenanceService _maintenanceService;
    private readonly IApplicationUpdateService _updateService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly SemaphoreSlim _themeChangeGate = new(1, 1);
    private readonly SemaphoreSlim _overlaySettingsGate = new(1, 1);
    private readonly CancellationTokenSource _viewModelLifetime = new();
    private CancellationTokenSource? _statusMessageLifetime;
    private SynchronizationContext? _uiContext;
    private Task? _sessionClockTask;
    private OverlayProfile? _globalOverlayProfile;
    private UpdateRelease? _availableUpdate;
    private UpdateInstallCapability _updateInstallCapability;
    private bool _loadingUpdatePreference;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LightThemeButtonLabel))]
    [NotifyPropertyChangedFor(nameof(DarkThemeButtonLabel))]
    [NotifyPropertyChangedFor(nameof(SystemThemeButtonLabel))]
    [NotifyPropertyChangedFor(nameof(IsLightTheme))]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme))]
    public partial ThemePreference CurrentTheme { get; set; } = ThemePreference.Light;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomePage))]
    [NotifyPropertyChangedFor(nameof(IsSettingsPage))]
    [NotifyPropertyChangedFor(nameof(IsGameCollectionPage))]
    [NotifyPropertyChangedFor(nameof(IsScanPage))]
    [NotifyPropertyChangedFor(nameof(IsGenericPage))]
    public partial NavigationItemViewModel? SelectedNavigationItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPerformanceOverlaySettingsSection))]
    [NotifyPropertyChangedFor(nameof(IsAppearanceSettingsSection))]
    [NotifyPropertyChangedFor(nameof(IsApplicationUpdateSettingsSection))]
    [NotifyPropertyChangedFor(nameof(IsDataMaintenanceSettingsSection))]
    [NotifyPropertyChangedFor(nameof(IsCompatibilitySettingsSection))]
    public partial SettingsSectionItemViewModel? SelectedSettingsSection { get; set; }

    [ObservableProperty]
    public partial string PageTitle { get; set; } = "首页";

    [ObservableProperty]
    public partial string PageDescription { get; set; } = "让散落在不同磁盘里的游戏回到一个清晰、安静的入口。";

    [ObservableProperty]
    public partial string EmptyTitle { get; set; } = "游戏库还是空的";

    [ObservableProperty]
    public partial string EmptyDescription { get; set; } = "从本地选择 EXE 或快捷方式，添加第一款游戏。";

    [ObservableProperty]
    public partial string EmptyGlyph { get; set; } = "\uE7FC";

    [ObservableProperty]
    public partial string StartupStatus { get; set; } = "正在准备本地空间";

    [ObservableProperty]
    public partial string StartupStatusGlyph { get; set; } = "\uE895";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryReady))]
    [NotifyPropertyChangedFor(nameof(HasNoGames))]
    [NotifyPropertyChangedFor(nameof(HasNoDisplayedGames))]
    public partial bool IsLibraryLoading { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLibraryError))]
    [NotifyPropertyChangedFor(nameof(HasNoGames))]
    [NotifyPropertyChangedFor(nameof(HasNoDisplayedGames))]
    public partial string? LibraryErrorMessage { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGame))]
    public partial GameCardViewModel? SelectedGame { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGames))]
    [NotifyPropertyChangedFor(nameof(SelectedGameCountText))]
    [NotifyPropertyChangedFor(nameof(SelectionModeButtonText))]
    [NotifyPropertyChangedFor(nameof(RemoveSelectedGamesText))]
    public partial bool IsSelectionMode { get; set; }

    [ObservableProperty]
    public partial bool IsOverlayEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayPreviewSummary))]
    public partial OverlayPositionOption SelectedOverlayPosition { get; set; } =
        new("右上角", OverlayPosition.TopRight);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayScaleText))]
    [NotifyPropertyChangedFor(nameof(OverlayPreviewSummary))]
    public partial int OverlayScalePercent { get; set; } = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayOpacityText))]
    [NotifyPropertyChangedFor(nameof(OverlayPreviewOpacity))]
    [NotifyPropertyChangedFor(nameof(OverlayPreviewSummary))]
    public partial double OverlayOpacityPercent { get; set; } = 88;

    [ObservableProperty]
    public partial bool ShowOverlayFps { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowOverlayCpu { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowOverlayGpu { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowOverlayRam { get; set; } = true;

    [ObservableProperty]
    public partial string OverlayToggleHotkey { get; set; } = "Ctrl+Shift+F12";

    [ObservableProperty]
    public partial bool HideOverlayWhenGameNotForeground { get; set; } = true;

    [ObservableProperty]
    public partial string OverlaySettingsStatus { get; set; } = "覆盖层设置仅保存在本机。";

    [ObservableProperty]
    public partial string OverlayCompatibilityStatus { get; set; } =
        "尚未运行兼容性检测。检测不会启动游戏，也不会请求管理员权限。";

    [ObservableProperty]
    public partial string OverlayRuntimeStatusText { get; set; } = "当前没有覆盖层会话。";

    [ObservableProperty]
    public partial bool IsCheckingOverlayCompatibility { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMaintenanceIdle))]
    public partial bool IsMaintenanceBusy { get; set; }

    [ObservableProperty]
    public partial string MaintenanceStatus { get; set; } =
        "每天最多自动备份一次；诊断包不包含游戏路径、数据库内容或凭据。";

    [ObservableProperty]
    public partial bool IsAutomaticUpdateCheckEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateIdle))]
    [NotifyPropertyChangedFor(nameof(IsUpdateActionEnabled))]
    public partial bool IsCheckingForUpdates { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateIdle))]
    [NotifyPropertyChangedFor(nameof(IsUpdateActionEnabled))]
    public partial bool IsPreparingUpdate { get; set; }

    [ObservableProperty]
    public partial string UpdateStatus { get; set; } = "尚未检查更新。";

    [ObservableProperty]
    public partial string UpdateLatestVersionText { get; set; } = "尚未检查";

    [ObservableProperty]
    public partial string UpdatePublishedAtText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateReleaseNotesDisplayText))]
    public partial string UpdateReleaseNotes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double UpdateProgressPercent { get; set; }

    [ObservableProperty]
    public partial bool HasUpdateProgress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateActionEnabled))]
    [NotifyPropertyChangedFor(nameof(UpdateActionLabel))]
    public partial bool HasAvailableUpdate { get; set; }

    [ObservableProperty]
    public partial bool IsUpdateNotificationOpen { get; set; }

    public MainWindowViewModel(
        IApplicationDataInitializer dataInitializer,
        IThemePreferenceStore themePreferenceStore,
        GameLibraryService gameLibraryService,
        OverlaySettingsService overlaySettingsService,
        IOverlayRuntimeCoordinator overlayRuntimeCoordinator,
        IApplicationMaintenanceService maintenanceService,
        IApplicationUpdateService updateService,
        ScanPageViewModel scan,
        ILogger<MainWindowViewModel> logger)
    {
        _dataInitializer = dataInitializer;
        _themePreferenceStore = themePreferenceStore;
        _gameLibraryService = gameLibraryService;
        _overlaySettingsService = overlaySettingsService;
        _overlayRuntimeCoordinator = overlayRuntimeCoordinator;
        _maintenanceService = maintenanceService;
        _updateService = updateService;
        Scan = scan;
        _logger = logger;

        NavigationItems =
        [
            new("首页", "\uE80F", "首页", "让散落在不同磁盘里的游戏回到一个清晰、安静的入口。", "游戏库还是空的", "从本地选择 EXE 或快捷方式，添加第一款游戏。", "\uE7FC"),
            new("游戏库", "\uE7FC", "游戏库", "所有本地游戏都会在这里按统一方式呈现。", "还没有游戏", "选择 EXE 或快捷方式，开始建立你的本地游戏库。", "\uE7FC"),
            new("收藏", "\uE734", "收藏", "把最常玩的游戏留在最顺手的位置。", "还没有收藏", "在游戏卡片或详情中点击星标即可收藏。", "\uE734"),
            new("最近游玩", "\uE81C", "最近游玩", "快速回到刚刚离开的世界。", "没有最近记录", "从 GameNest 启动游戏后，最近记录会显示在这里。", "\uE81C"),
            new("正在运行", "\uE768", "正在运行", "查看已识别的实际游戏进程与本次会话。", "当前没有运行中的游戏", "启动游戏后，GameNest 会识别直接进程或其派生进程。", "\uE768"),
            new("扫描与导入", "\uE896", "扫描与导入", "管理扫描范围，查看可解释的候选并确认导入。", "还没有扫描结果", "添加一个目录，执行快速扫描或深度扫描。", "\uE896"),
            new("设置", "\uE713", "设置", "调整外观、性能覆盖层与本地隐私选项。", "本地设置已可用", "你可以调整主题、覆盖层和兼容性选项。", "\uE713"),
        ];

        Games = [];
        DisplayedGames = [];
        OverlayPositionOptions =
        [
            new("左上角", OverlayPosition.TopLeft),
            new("右上角", OverlayPosition.TopRight),
            new("左下角", OverlayPosition.BottomLeft),
            new("右下角", OverlayPosition.BottomRight),
        ];
        OverlayScaleOptions = [75, 100, 125, 150];
        SettingsSections =
        [
            new(SettingsSectionId.PerformanceOverlay, "性能覆盖层", "显示、指标与快捷键", "\uE9D9"),
            new(SettingsSectionId.Appearance, "应用外观", "浅色、深色或跟随系统", "\uE790"),
            new(SettingsSectionId.ApplicationUpdate, "应用更新", "版本与自动检查", "\uE895"),
            new(SettingsSectionId.DataMaintenance, "数据与维护", "备份、缓存与诊断", "\uE74E"),
            new(SettingsSectionId.Compatibility, "兼容性检测", "覆盖层能力与权限", "\uE83D"),
        ];
        _gameLibraryService.RuntimeStatusChanged += HandleRuntimeStatusChanged;
        _overlayRuntimeCoordinator.StatusChanged += HandleOverlayRuntimeStatusChanged;
        Scan.CandidatesImported += HandleCandidatesImported;
        SelectedNavigationItem = NavigationItems[0];
        SelectedSettingsSection = SettingsSections[0];
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public event Action? UpdateInstallerStarted;

    public event Action<Uri>? OpenUpdatePageRequested;

    public event Action<GameCardViewModel>? FocusGameRequested;

    public ObservableCollection<GameCardViewModel> Games { get; }

    public ObservableCollection<RecentPlayGroupViewModel> RecentPlayGroups { get; } = [];

    [ObservableProperty]
    public partial ObservableCollection<GameCardViewModel> DisplayedGames { get; set; }

    public IReadOnlyList<OverlayPositionOption> OverlayPositionOptions { get; }

    public IReadOnlyList<int> OverlayScaleOptions { get; }

    public IReadOnlyList<SettingsSectionItemViewModel> SettingsSections { get; }

    public ScanPageViewModel Scan { get; }

    public bool IsHomePage => SelectedNavigationItem?.Label == "首页";

    public bool IsSettingsPage => SelectedNavigationItem?.Label == "设置";

    public bool IsPerformanceOverlaySettingsSection =>
        SelectedSettingsSection?.Id == SettingsSectionId.PerformanceOverlay;

    public bool IsAppearanceSettingsSection =>
        SelectedSettingsSection?.Id == SettingsSectionId.Appearance;

    public bool IsApplicationUpdateSettingsSection =>
        SelectedSettingsSection?.Id == SettingsSectionId.ApplicationUpdate;

    public bool IsDataMaintenanceSettingsSection =>
        SelectedSettingsSection?.Id == SettingsSectionId.DataMaintenance;

    public bool IsCompatibilitySettingsSection =>
        SelectedSettingsSection?.Id == SettingsSectionId.Compatibility;

    public bool IsGameCollectionPage => SelectedNavigationItem?.Label is "游戏库" or "收藏" or "最近游玩" or "正在运行";

    public bool IsScanPage => SelectedNavigationItem?.Label == "扫描与导入";

    public bool IsGenericPage => !IsHomePage && !IsSettingsPage && !IsGameCollectionPage && !IsScanPage;

    public bool HasAnyGames => Games.Count > 0;

    public bool HasNoGames => IsLibraryReady && !HasLibraryError && !HasAnyGames;

    public bool HasDisplayedGames => DisplayedGames.Count > 0;

    public bool HasNoDisplayedGames => IsLibraryReady && !HasLibraryError && !HasDisplayedGames;

    public bool HasSelectedGame => SelectedGame is not null;

    public bool HasSelectedGames => Games.Any(static game => game.IsSelected);

    public string SelectedGameCountText => $"已选择 {Games.Count(static game => game.IsSelected)} 项";

    public string SelectionModeButtonText => IsSelectionMode ? "完成选择" : "多选";

    public string RemoveSelectedGamesText => HasSelectedGames
        ? $"移除已选（{Games.Count(static game => game.IsSelected)}）"
        : "移除已选";

    public void UpdateGameSelection(IEnumerable<GameCardViewModel> selectedGames)
    {
        ArgumentNullException.ThrowIfNull(selectedGames);

        var selectedIds = selectedGames.Select(static game => game.Id).ToHashSet();
        foreach (var game in Games)
        {
            game.IsSelected = selectedIds.Contains(game.Id);
        }

        OnPropertyChanged(nameof(HasSelectedGames));
        OnPropertyChanged(nameof(SelectedGameCountText));
        OnPropertyChanged(nameof(RemoveSelectedGamesText));
    }

    public bool IsLibraryPage => SelectedNavigationItem?.Label == "游戏库";

    public bool IsRecentPage => SelectedNavigationItem?.Label == "最近游玩";

    public bool IsNotRecentPage => !IsRecentPage;

    public string CollectionActionText => IsLibraryPage ? "添加游戏" : "前往游戏库";

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    partial void OnStatusMessageChanged(string? value)
    {
        _statusMessageLifetime?.Cancel();
        _statusMessageLifetime?.Dispose();
        _statusMessageLifetime = null;

        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("正在", StringComparison.Ordinal))
        {
            return;
        }

        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_viewModelLifetime.Token);
        _statusMessageLifetime = lifetime;
        _ = ClearStatusMessageAfterDelayAsync(value, lifetime.Token);
    }

    private async Task ClearStatusMessageAfterDelayAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (_uiContext is null)
        {
            if (string.Equals(StatusMessage, message, StringComparison.Ordinal))
            {
                StatusMessage = null;
            }

            return;
        }

        _uiContext.Post(
            static state =>
            {
                var request = (StatusMessageClearRequest)state!;
                if (string.Equals(request.ViewModel.StatusMessage, request.Message, StringComparison.Ordinal))
                {
                    request.ViewModel.StatusMessage = null;
                }
            },
            new StatusMessageClearRequest(this, message));
    }

    public bool IsMaintenanceIdle => !IsMaintenanceBusy;

    public bool IsUpdateIdle => !IsCheckingForUpdates && !IsPreparingUpdate;

    public bool IsUpdateActionEnabled => HasAvailableUpdate && IsUpdateIdle;

    public string UpdateActionLabel => _updateInstallCapability == UpdateInstallCapability.Ready
        ? "下载并安装"
        : "打开下载页";

    public string UpdateCurrentVersionText => ApplicationVersion.Format(_updateService.CurrentVersion);

    public string UpdateReleaseNotesDisplayText => string.IsNullOrWhiteSpace(UpdateReleaseNotes)
        ? "暂无可用的发布说明。"
        : UpdateReleaseNotes;

    public bool IsLibraryReady => !IsLibraryLoading;

    public bool HasLibraryError => !string.IsNullOrWhiteSpace(LibraryErrorMessage);

    public GameCardViewModel? HeroGame => Games
        .Where(static game => game.LastPlayedUtc is not null)
        .OrderByDescending(static game => game.LastPlayedUtc)
        .FirstOrDefault();

    public bool HasHeroGame => HeroGame is not null;

    public bool HasNoHeroGame => !HasHeroGame;

    public string GameCountText => $"{Games.Count} 款游戏";

    public string DisplayedGameCountText => $"{DisplayedGames.Count} 款游戏";

    public bool IsLightTheme => CurrentTheme == ThemePreference.Light;

    public bool IsDarkTheme => CurrentTheme == ThemePreference.Dark;

    public bool IsSystemTheme => CurrentTheme == ThemePreference.System;

    public string LightThemeButtonLabel => CurrentTheme == ThemePreference.Light ? "✓  浅色" : "浅色";

    public string DarkThemeButtonLabel => CurrentTheme == ThemePreference.Dark ? "✓  深色" : "深色";

    public string SystemThemeButtonLabel => CurrentTheme == ThemePreference.System ? "✓  跟随系统" : "跟随系统";

    public string OverlayScaleText => $"{OverlayScalePercent}%";

    public string OverlayOpacityText => $"{Math.Round(OverlayOpacityPercent):0}%";

    public double OverlayPreviewOpacity => OverlayOpacityPercent / 100d;

    public string OverlayPreviewSummary =>
        $"{SelectedOverlayPosition.Label} · {OverlayScalePercent}% · {Math.Round(OverlayOpacityPercent):0}% 不透明度";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _uiContext = SynchronizationContext.Current;
        _sessionClockTask ??= RefreshSessionClocksAsync(_viewModelLifetime.Token);

        try
        {
            IsLibraryLoading = true;
            LibraryErrorMessage = null;
            await _dataInitializer.InitializeAsync(cancellationToken);
            CurrentTheme = await _themePreferenceStore.GetAsync(cancellationToken);
            _loadingUpdatePreference = true;
            var updatePreference = await _updateService.GetPreferenceAsync(cancellationToken);
            IsAutomaticUpdateCheckEnabled = updatePreference.AutomaticCheckEnabled;
            _loadingUpdatePreference = false;
            await LoadOverlaySettingsAsync(cancellationToken);
            await _overlayRuntimeCoordinator.InitializeAsync(cancellationToken);
            var recoveredSessionCount = await _gameLibraryService
                .RecoverInterruptedSessionsAsync(cancellationToken);
            await ReloadGamesAsync(cancellationToken);
            await Scan.InitializeAsync(cancellationToken);
            StartupStatus = recoveredSessionCount == 0
                ? "本地空间已就绪"
                : $"已恢复 {recoveredSessionCount} 个中断会话";
            StartupStatusGlyph = "\uE930";
            IsLibraryLoading = false;
            _ = RunAutomaticBackupAsync(_viewModelLifetime.Token);
            _ = CheckForUpdatesCoreAsync(force: false, _viewModelLifetime.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StartupStatus = "初始化已取消";
            StartupStatusGlyph = "\uE711";
            IsLibraryLoading = false;
        }
        catch (Exception exception)
        {
            ApplicationInitializationFailed(_logger, exception);
            StartupStatus = "本地空间暂不可用，请查看日志";
            StartupStatusGlyph = "\uE783";
            StatusMessage = "无法加载本地游戏库，请查看日志后重试。";
            LibraryErrorMessage = "本地游戏库暂时无法读取。请确认数据目录可访问后重试。";
            IsLibraryLoading = false;
        }
    }

    public async Task<GameCardViewModel?> AddGameAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            StatusMessage = null;
            var game = await _gameLibraryService.AddAsync(sourcePath, new(), cancellationToken);
            var card = CreateGameCard(game);
            Games.Add(card);
            SortGames();
            SelectedNavigationItem = NavigationItems[1];
            ApplyFilter();
            SelectedGame = card;
            StatusMessage = $"已添加“{game.Title}”。";
            _ = RefreshGameAssetsAsync(card, _viewModelLifetime.Token);
            return card;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            ReportGameOperationFailure("添加游戏", exception);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> UpdateGameAsync(
        GameCardViewModel card,
        GameEditorInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(card);
        try
        {
            IsBusy = true;
            StatusMessage = null;
            var updated = await _gameLibraryService.UpdateAsync(card.Id, input, cancellationToken);
            card.Update(updated);
            SortGames();
            ApplyFilter();
            SelectedGame = Games.FirstOrDefault(game => game.Id == updated.Id);
            StatusMessage = $"已保存“{updated.Title}”的本地编辑。";
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            ReportGameOperationFailure("编辑游戏", exception);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> RemoveGameAsync(
        GameCardViewModel card,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(card);
        try
        {
            IsBusy = true;
            StatusMessage = null;
            if (!await _gameLibraryService.RemoveAsync(card.Id, cancellationToken))
            {
                StatusMessage = "该游戏已经不在游戏库中。";
                return false;
            }

            var previousIndex = DisplayedGames.IndexOf(card);
            Games.Remove(card);
            ApplyFilter();
            var next = previousIndex >= 0 && DisplayedGames.Count > 0
                ? DisplayedGames[Math.Min(previousIndex, DisplayedGames.Count - 1)]
                : null;
            SelectedGame = next;
            if (next is not null)
            {
                FocusGameRequested?.Invoke(next);
            }
            StatusMessage = $"已从游戏库移除“{card.Title}”；原始游戏文件未被删除。";
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            ReportGameOperationFailure("移除游戏", exception);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ImportCoverAsync(
        GameCardViewModel card,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(card);
        try
        {
            IsBusy = true;
            StatusMessage = $"正在优化“{card.Title}”的封面…";
            var updated = await _gameLibraryService
                .ImportCoverAsync(card.Id, sourcePath, cancellationToken);
            card.Update(updated);
            NotifyCollectionStateChanged();
            StatusMessage = $"已更新“{card.Title}”的本地封面。";
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            ReportGameOperationFailure("导入封面", exception);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> RemoveCoverAsync(
        GameCardViewModel card,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(card);
        try
        {
            IsBusy = true;
            var updated = await _gameLibraryService.RemoveCoverAsync(card.Id, cancellationToken);
            card.Update(updated);
            NotifyCollectionStateChanged();
            StatusMessage = $"已移除“{card.Title}”的封面；不会再次自动匹配本地图片。";
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            ReportGameOperationFailure("移除封面", exception);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gameLibraryService.RuntimeStatusChanged -= HandleRuntimeStatusChanged;
        _overlayRuntimeCoordinator.StatusChanged -= HandleOverlayRuntimeStatusChanged;
        Scan.CandidatesImported -= HandleCandidatesImported;
        _statusMessageLifetime?.Cancel();
        _statusMessageLifetime?.Dispose();
        _viewModelLifetime.Cancel();
        Scan.Dispose();
        _themeChangeGate.Dispose();
        _overlaySettingsGate.Dispose();
        _viewModelLifetime.Dispose();
        _disposed = true;
    }

    private async void HandleCandidatesImported(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        try
        {
            await ReloadGamesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            ReportGameOperationFailure("刷新扫描导入结果", exception);
        }
    }

    partial void OnSelectedNavigationItemChanged(NavigationItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        PageTitle = value.PageTitle;
        PageDescription = value.PageDescription;
        EmptyTitle = value.EmptyTitle;
        EmptyDescription = value.EmptyDescription;
        EmptyGlyph = value.EmptyGlyph;
        IsSelectionMode = false;
        ClearGameSelection();
        OnPropertyChanged(nameof(IsLibraryPage));
        OnPropertyChanged(nameof(IsRecentPage));
        OnPropertyChanged(nameof(IsNotRecentPage));
        OnPropertyChanged(nameof(CollectionActionText));
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !IsGameCollectionPage)
        {
            SelectedNavigationItem = NavigationItems[1];
        }

        ApplyFilter();
    }

    partial void OnIsAutomaticUpdateCheckEnabledChanged(bool value)
    {
        if (!_loadingUpdatePreference && !_disposed)
        {
            _ = SaveAutomaticUpdatePreferenceAsync(value, _viewModelLifetime.Token);
        }
    }

    [RelayCommand]
    private void OpenSettings() => SelectedNavigationItem = NavigationItems[^1];

    [RelayCommand]
    private void OpenLibrary() => SelectedNavigationItem = NavigationItems[1];

    [RelayCommand]
    private void OpenHeroDetails()
    {
        var hero = HeroGame;
        SelectedNavigationItem = NavigationItems[1];
        if (hero is null)
        {
            return;
        }

        SelectedGame = hero;
        FocusGameRequested?.Invoke(hero);
    }

    [RelayCommand]
    private void ToggleSelectionMode()
    {
        IsSelectionMode = !IsSelectionMode;
        if (!IsSelectionMode)
        {
            ClearGameSelection();
        }
    }

    public async Task<IReadOnlyList<GameCoverCandidate>> SearchOnlineCoversAsync(
        GameCardViewModel card,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(card);
        try
        {
            IsBusy = true;
            StatusMessage = $"正在查找“{card.Title}”的在线封面…";
            return await _gameLibraryService.SearchOnlineCoversAsync(card.Title, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ReportGameOperationFailure("查找在线封面", exception);
            return [];
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ApplyOnlineCoverAsync(
        GameCardViewModel card,
        GameCoverCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(candidate);
        try
        {
            IsBusy = true;
            StatusMessage = $"正在下载“{candidate.Title}”的封面…";
            var updated = await _gameLibraryService.ApplyOnlineCoverAsync(card.Id, candidate, cancellationToken);
            card.Update(updated);
            NotifyCollectionStateChanged();
            StatusMessage = $"已使用 Steam 商店封面：{candidate.Title}。";
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ReportGameOperationFailure("应用在线封面", exception);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<int> FetchAllMissingCoversAsync(CancellationToken cancellationToken)
    {
        var targets = Games
            .Where(static game => !game.HasCover && !game.IsCoverManuallyDisabled)
            .ToArray();
        if (targets.Length == 0)
        {
            StatusMessage = "没有需要获取的游戏封面。";
            return 0;
        }

        try
        {
            IsBusy = true;
            var acquired = 0;
            for (var index = 0; index < targets.Length; index++)
            {
                var card = targets[index];
                StatusMessage = $"正在获取游戏封面（{index + 1}/{targets.Length}）：{card.Title}";
                try
                {
                    var updated = await _gameLibraryService
                        .RefreshAssetsAsync(card.Id, cancellationToken);
                    card.Update(updated);
                    if (card.HasCover)
                    {
                        acquired++;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    GameOperationFailed(_logger, $"获取“{card.Title}”的在线封面", exception);
                }
            }

            NotifyCollectionStateChanged();
            StatusMessage = acquired == 0
                ? "未找到可自动使用的在线封面；可在游戏详情中手动选择候选。"
                : $"已获取 {acquired} 个游戏封面。";
            return acquired;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "获取游戏封面已取消。";
            return 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<int> RemoveSelectedGamesAsync(CancellationToken cancellationToken)
    {
        var selected = DisplayedGames.Where(static game => game.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return 0;
        }

        var firstIndex = DisplayedGames.IndexOf(selected[0]);
        try
        {
            IsBusy = true;
            foreach (var card in selected)
            {
                if (await _gameLibraryService.RemoveAsync(card.Id, cancellationToken).ConfigureAwait(false))
                {
                    Games.Remove(card);
                }
            }

            ApplyFilter();
            ClearGameSelection();
            IsSelectionMode = false;
            var next = DisplayedGames.Count == 0
                ? null
                : DisplayedGames[Math.Min(firstIndex, DisplayedGames.Count - 1)];
            SelectedGame = next;
            if (next is not null)
            {
                FocusGameRequested?.Invoke(next);
            }

            StatusMessage = $"已从游戏库移除 {selected.Length} 项；原始游戏文件未被删除。";
            return selected.Length;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ReportGameOperationFailure("批量移除游戏", exception);
            return 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task CheckForUpdatesAsync(CancellationToken cancellationToken) =>
        CheckForUpdatesCoreAsync(force: true, cancellationToken);

    [RelayCommand]
    private async Task InstallOrOpenUpdateAsync(CancellationToken cancellationToken)
    {
        var release = _availableUpdate;
        if (release is null || IsPreparingUpdate)
        {
            return;
        }

        if (_updateInstallCapability != UpdateInstallCapability.Ready)
        {
            OpenUpdatePageRequested?.Invoke(release.ReleasePageUri);
            return;
        }

        try
        {
            IsPreparingUpdate = true;
            HasUpdateProgress = true;
            var progress = new Progress<UpdateProgress>(value =>
            {
                UpdateStatus = value.Message;
                UpdateProgressPercent = value.Percent ?? 0;
                HasUpdateProgress = value.Percent.HasValue;
            });
            var prepared = await _updateService.PrepareAsync(release, progress, cancellationToken);
            var launched = await _updateService.LaunchInstallerAsync(
                prepared,
                Environment.ProcessId,
                cancellationToken);
            UpdateStatus = launched.Message;
            if (launched.Started)
            {
                UpdateInstallerStarted?.Invoke();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            UpdateStatus = "更新准备已取消。";
        }
        catch (Exception exception)
        {
            GameOperationFailed(_logger, "准备应用更新", exception);
            UpdateStatus = exception is InvalidDataException or InvalidOperationException
                ? exception.Message
                : "无法准备更新，请查看日志或改用 GitHub 下载页。";
            if (exception is InvalidOperationException { InnerException: UnauthorizedAccessException })
            {
                OpenUpdatePageRequested?.Invoke(release.ReleasePageUri);
            }
        }
        finally
        {
            IsPreparingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task RetryLibraryLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsLibraryLoading = true;
            LibraryErrorMessage = null;
            await ReloadGamesAsync(cancellationToken);
            StatusMessage = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            GameOperationFailed(_logger, "重新加载游戏库", exception);
            LibraryErrorMessage = "仍然无法读取本地游戏库，请查看日志中的详细原因。";
        }
        finally
        {
            IsLibraryLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(
        GameCardViewModel? card,
        CancellationToken cancellationToken)
    {
        if (card is null)
        {
            return;
        }

        try
        {
            var updated = await _gameLibraryService
                .SetFavoriteAsync(card.Id, !card.IsFavorite, cancellationToken);
            card.Update(updated);
            ApplyFilter();
            SelectedGame = Games.FirstOrDefault(game => game.Id == updated.Id);
            StatusMessage = updated.IsFavorite
                ? $"已收藏“{updated.Title}”。"
                : $"已取消收藏“{updated.Title}”。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportGameOperationFailure("更新收藏", exception);
        }
    }

    [RelayCommand]
    private async Task LaunchGameAsync(
        GameCardViewModel? card,
        CancellationToken cancellationToken)
    {
        if (card is null || card.RuntimeState != GameRuntimeState.NotRunning)
        {
            return;
        }

        try
        {
            StatusMessage = null;
            await _gameLibraryService.LaunchAsync(card.Id, cancellationToken);
            var storedGames = await _gameLibraryService.GetGamesAsync(new(), cancellationToken);
            var stored = storedGames.First(game => game.Id == card.Id);
            card.Update(stored);
            StatusMessage = $"已启动“{card.Title}”，正在识别实际游戏进程。";
            ApplyFilter();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            card.UpdateRuntime(
                new GameRuntimeSnapshot(
                    card.Id,
                    GameRuntimeState.NotRunning,
                    null,
                    GameProcessConfidence.Unconfirmed,
                    null,
                    []));
            ReportGameOperationFailure("启动游戏", exception);
        }
    }

    public async Task<GameStopResult?> StopGameAsync(
        GameCardViewModel card,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(card);
        try
        {
            StatusMessage = force
                ? $"正在强制结束“{card.Title}”…"
                : $"正在请求“{card.Title}”正常关闭…";
            var result = await _gameLibraryService.StopAsync(card.Id, force, cancellationToken);
            StatusMessage = result.Message;
            if (result.Outcome == GameStopOutcome.Stopped)
            {
                await RefreshGameAfterSessionAsync(card.Id, cancellationToken);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            ReportGameOperationFailure(force ? "强制结束游戏" : "停止游戏", exception);
            return null;
        }
    }

    [RelayCommand]
    private Task UseLightThemeAsync(CancellationToken cancellationToken) =>
        ChangeThemeAsync(ThemePreference.Light, cancellationToken);

    [RelayCommand]
    private Task UseDarkThemeAsync(CancellationToken cancellationToken) =>
        ChangeThemeAsync(ThemePreference.Dark, cancellationToken);

    [RelayCommand]
    private Task UseSystemThemeAsync(CancellationToken cancellationToken) =>
        ChangeThemeAsync(ThemePreference.System, cancellationToken);

    [RelayCommand]
    private void OpenCompatibilitySettings()
    {
        SelectedSettingsSection = SettingsSections.First(section =>
            section.Id == SettingsSectionId.Compatibility);
    }

    [RelayCommand]
    private async Task SaveOverlaySettingsAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _overlaySettingsGate.WaitAsync(cancellationToken);
        try
        {
            OverlaySettingsStatus = "正在验证快捷键并保存设置…";
            var input = CreateOverlayInput();
            _globalOverlayProfile = await _overlaySettingsService
                .SaveGlobalAsync(input, cancellationToken);
            await _overlayRuntimeCoordinator.RefreshProfileAsync(cancellationToken);
            OverlaySettingsStatus = "覆盖层设置已保存并应用。";
            StatusMessage = "性能覆盖层设置已保存。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            OverlaySettingsStatus = "保存已取消。";
        }
        catch (Exception exception)
        {
            GameOperationFailed(_logger, "保存覆盖层设置", exception);
            OverlaySettingsStatus = exception is ArgumentException or InvalidOperationException
                ? exception.Message
                : "无法保存覆盖层设置，请查看日志。";
        }
        finally
        {
            _overlaySettingsGate.Release();
        }
    }

    [RelayCommand]
    private async Task CheckOverlayCompatibilityAsync(CancellationToken cancellationToken)
    {
        if (IsCheckingOverlayCompatibility)
        {
            return;
        }

        try
        {
            IsCheckingOverlayCompatibility = true;
            OverlayCompatibilityStatus = "正在以普通权限检测 PresentMon 和 Windows 性能计数器…";
            var report = await _overlaySettingsService.CheckCapabilityAsync(cancellationToken);
            OverlayCompatibilityStatus =
                $"FPS：{report.Fps.Message}\n" +
                $"CPU：{report.Cpu.Message}\n" +
                $"GPU：{report.Gpu.Message}\n" +
                $"RAM：{report.Ram.Message}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            OverlayCompatibilityStatus = "兼容性检测已取消。";
        }
        catch (Exception exception)
        {
            GameOperationFailed(_logger, "检测覆盖层兼容性", exception);
            OverlayCompatibilityStatus = "兼容性检测失败，请查看日志；不会自动请求管理员权限。";
        }
        finally
        {
            IsCheckingOverlayCompatibility = false;
        }
    }

    [RelayCommand]
    private async Task CreateBackupAsync(CancellationToken cancellationToken)
    {
        if (IsMaintenanceBusy)
        {
            return;
        }

        try
        {
            IsMaintenanceBusy = true;
            MaintenanceStatus = "正在创建一致性数据库备份…";
            var result = await _maintenanceService.CreateManualBackupAsync(cancellationToken);
            MaintenanceStatus = $"备份已保存：{Path.GetFileName(result.BackupFile)}";
            StatusMessage = "本地数据库备份已完成。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MaintenanceStatus = "备份已取消。";
        }
        catch (Exception exception)
        {
            GameOperationFailed(_logger, "创建数据库备份", exception);
            MaintenanceStatus = "无法创建备份，请查看日志并确认数据目录可写。";
        }
        finally
        {
            IsMaintenanceBusy = false;
        }
    }

    [RelayCommand]
    private async Task CleanupImageCacheAsync(CancellationToken cancellationToken)
    {
        if (IsMaintenanceBusy)
        {
            return;
        }

        try
        {
            IsMaintenanceBusy = true;
            MaintenanceStatus = "正在核对数据库引用并清理孤立图片缓存…";
            var result = await _maintenanceService.CleanupImageCacheAsync(cancellationToken);
            MaintenanceStatus = result.DeletedFileCount == 0
                ? "缓存已检查，没有发现可清理的孤立文件。"
                : $"已清理 {result.DeletedFileCount} 个文件，释放 {FormatBytes(result.ReclaimedBytes)}。";
            StatusMessage = "本地图片缓存清理已完成。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MaintenanceStatus = "缓存清理已取消。";
        }
        catch (Exception exception)
        {
            GameOperationFailed(_logger, "清理图片缓存", exception);
            MaintenanceStatus = "无法完成缓存清理，请查看日志。";
        }
        finally
        {
            IsMaintenanceBusy = false;
        }
    }

    public async Task ExportDiagnosticsAsync(
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        if (IsMaintenanceBusy)
        {
            return;
        }

        try
        {
            IsMaintenanceBusy = true;
            MaintenanceStatus = "正在生成脱敏诊断包…";
            var result = await _maintenanceService
                .ExportDiagnosticsAsync(destinationDirectory, cancellationToken);
            MaintenanceStatus = $"诊断包已导出：{Path.GetFileName(result.ArchiveFile)}";
            StatusMessage = "脱敏诊断信息已导出。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MaintenanceStatus = "诊断导出已取消。";
        }
        catch (Exception exception)
        {
            GameOperationFailed(_logger, "导出诊断信息", exception);
            MaintenanceStatus = "无法导出诊断信息，请查看日志并确认目标目录可写。";
        }
        finally
        {
            IsMaintenanceBusy = false;
        }
    }

    public Task<OverlayProfile> GetGlobalOverlayProfileAsync(CancellationToken cancellationToken) =>
        _overlaySettingsService.GetGlobalAsync(cancellationToken);

    public Task<OverlayProfile?> GetGameOverlayProfileAsync(
        Guid gameId,
        CancellationToken cancellationToken) =>
        _overlaySettingsService.GetForGameAsync(gameId, cancellationToken);

    public async Task<bool> SaveGameOverlayProfileAsync(
        Guid gameId,
        bool useGameOverride,
        OverlayProfileEditorInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            if (useGameOverride)
            {
                await _overlaySettingsService.SaveForGameAsync(gameId, input, cancellationToken);
                StatusMessage = "该游戏的覆盖层独立设置已保存。";
            }
            else
            {
                await _overlaySettingsService.RemoveForGameAsync(gameId, cancellationToken);
                StatusMessage = "该游戏已恢复使用全局覆盖层设置。";
            }

            await _overlayRuntimeCoordinator.RefreshProfileAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            ReportGameOperationFailure("保存游戏覆盖层设置", exception);
            return false;
        }
    }

    private async Task CheckForUpdatesCoreAsync(bool force, CancellationToken cancellationToken)
    {
        if (IsCheckingForUpdates || IsPreparingUpdate)
        {
            return;
        }

        try
        {
            IsCheckingForUpdates = true;
            if (force)
            {
                UpdateStatus = "正在连接 GitHub 检查正式版本…";
            }

            var result = await _updateService.CheckAsync(force, cancellationToken);
            _updateInstallCapability = result.InstallCapability;
            _availableUpdate = result.Release;
            HasAvailableUpdate = result.Availability == UpdateAvailability.Available && result.Release is not null;
            IsUpdateNotificationOpen = HasAvailableUpdate;
            UpdateStatus = result.Message;
            UpdateLatestVersionText = result.Release is null
                ? result.Availability == UpdateAvailability.UpToDate
                    ? UpdateCurrentVersionText
                    : "尚不可用"
                : ApplicationVersion.Format(result.Release.Version);
            UpdatePublishedAtText = result.Release is null
                ? string.Empty
                : $"发布于 {result.Release.PublishedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
            UpdateReleaseNotes = result.Release?.ReleaseNotes ?? string.Empty;
            if (HasAvailableUpdate && result.InstallCapability != UpdateInstallCapability.Ready)
            {
                UpdateStatus = result.InstallCapability switch
                {
                    UpdateInstallCapability.TrustedSigningKeyUnavailable =>
                        "发现新版本；当前 0.2.0 尚无内置生产公钥，请从 GitHub 下载页手动更新。",
                    UpdateInstallCapability.NotPortable =>
                        "发现新版本；当前安装不是可验证的便携版目录，请手动更新。",
                    UpdateInstallCapability.ProgramDirectoryNotWritable =>
                        "发现新版本；普通权限无法写入当前目录，请手动更新。",
                    _ => "发现新版本，但当前系统不支持自动安装。",
                };
            }

            OnPropertyChanged(nameof(UpdateActionLabel));
            OnPropertyChanged(nameof(IsUpdateActionEnabled));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (force)
            {
                UpdateStatus = "检查更新已取消。";
            }
        }
        catch (Exception exception)
        {
            GameOperationFailed(_logger, "检查应用更新", exception);
            UpdateStatus = "暂时无法检查更新；本地游戏库不受影响。";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private async Task SaveAutomaticUpdatePreferenceAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        try
        {
            await _updateService.SetAutomaticCheckEnabledAsync(enabled, cancellationToken);
            UpdateStatus = enabled
                ? "已开启自动检查，每 24 小时最多请求 GitHub 一次。"
                : "已关闭自动检查；仍可随时手动检查。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            GameOperationFailed(_logger, "保存自动更新偏好", exception);
            UpdateStatus = "无法保存自动检查设置，请稍后重试。";
        }
    }

    private async Task RunAutomaticBackupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _maintenanceService.CreateAutomaticBackupAsync(cancellationToken);
            MaintenanceStatus = result.Created
                ? $"今天的自动备份已完成：{Path.GetFileName(result.BackupFile)}"
                : "今天已有可用备份；下次启动时会再次检查。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            GameOperationFailed(_logger, "创建启动自动备份", exception);
            MaintenanceStatus = "自动备份暂未完成；游戏库仍可使用，可稍后手动重试。";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        return $"{bytes / (1024d * 1024d):0.0} MB";
    }

    private async Task ReloadGamesAsync(CancellationToken cancellationToken)
    {
        var games = await _gameLibraryService.GetGamesAsync(new(), cancellationToken);
        Games.Clear();
        foreach (var game in games)
        {
            var card = CreateGameCard(game);
            var runtime = _gameLibraryService.GetRuntime(game.Id);
            if (runtime is not null)
            {
                card.UpdateRuntime(runtime);
            }

            Games.Add(card);
        }

        ApplyFilter();
        NotifyCollectionStateChanged();
    }

    private void ApplyFilter()
    {
        if (DisplayedGames is null || Games is null)
        {
            return;
        }

        IEnumerable<GameCardViewModel> filtered = Games;
        filtered = SelectedNavigationItem?.Label switch
        {
            "收藏" => filtered.Where(static game => game.IsFavorite),
            "最近游玩" => filtered.Where(static game => game.LastPlayedUtc is not null)
                .OrderByDescending(static game => game.LastPlayedUtc),
            "正在运行" => filtered.Where(
                static game => game.RuntimeState is
                    GameRuntimeState.Launching or GameRuntimeState.Running or GameRuntimeState.Stopping),
            _ => filtered,
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var searchText = SearchText.Trim();
            filtered = filtered.Where(
                game => game.Title.Contains(searchText, StringComparison.CurrentCultureIgnoreCase));
        }

        var selectedId = SelectedGame?.Id;
        var displayed = filtered.ToArray();
        DisplayedGames = new ObservableCollection<GameCardViewModel>(displayed);
        RebuildRecentGroups(displayed);

        SelectedGame = selectedId is null
            ? DisplayedGames.FirstOrDefault()
            : DisplayedGames.FirstOrDefault(game => game.Id == selectedId) ?? DisplayedGames.FirstOrDefault();
        NotifyCollectionStateChanged();
    }

    private void SortGames()
    {
        var sorted = Games.OrderBy(static game => game.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
        Games.Clear();
        foreach (var card in sorted)
        {
            Games.Add(card);
        }
    }

    private GameCardViewModel CreateGameCard(Game game)
    {
        var card = new GameCardViewModel(
            game,
            (item, cancellationToken) => ToggleFavoriteAsync(item, cancellationToken),
            (item, cancellationToken) => LaunchGameAsync(item, cancellationToken));
        card.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GameCardViewModel.IsSelected))
            {
                OnPropertyChanged(nameof(HasSelectedGames));
                OnPropertyChanged(nameof(SelectedGameCountText));
                OnPropertyChanged(nameof(RemoveSelectedGamesText));
            }
        };
        return card;
    }

    private void ClearGameSelection()
    {
        foreach (var card in Games.Where(static game => game.IsSelected))
        {
            card.IsSelected = false;
        }
    }

    private void RebuildRecentGroups(IReadOnlyList<GameCardViewModel> displayed)
    {
        RecentPlayGroups.Clear();
        if (!IsRecentPage)
        {
            return;
        }

        var today = DateTimeOffset.Now.Date;
        var sevenDaysAgo = today.AddDays(-6);
        var groups = displayed
            .Where(static game => game.LastPlayedUtc is not null)
            .GroupBy(game =>
            {
                var played = game.LastPlayedUtc!.Value.ToLocalTime().Date;
                return played == today ? "今天" : played >= sevenDaysAgo ? "近 7 天" : "更早";
            })
            .OrderBy(group => group.Key switch { "今天" => 0, "近 7 天" => 1, _ => 2 });
        foreach (var group in groups)
        {
            RecentPlayGroups.Add(new RecentPlayGroupViewModel(
                group.Key,
                group.OrderByDescending(static game => game.LastPlayedUtc)));
        }
    }

    private async Task RefreshGameAssetsAsync(
        GameCardViewModel card,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _gameLibraryService.RefreshAssetsAsync(card.Id, cancellationToken);
            card.Update(updated);
            NotifyCollectionStateChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            GameOperationFailed(_logger, "后台准备图标和封面", exception);
        }
    }

    private void NotifyCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasAnyGames));
        OnPropertyChanged(nameof(HasNoGames));
        OnPropertyChanged(nameof(HasDisplayedGames));
        OnPropertyChanged(nameof(HasNoDisplayedGames));
        OnPropertyChanged(nameof(GameCountText));
        OnPropertyChanged(nameof(DisplayedGameCountText));
        OnPropertyChanged(nameof(HeroGame));
        OnPropertyChanged(nameof(HasHeroGame));
        OnPropertyChanged(nameof(HasNoHeroGame));
        OnPropertyChanged(nameof(HasSelectedGames));
        OnPropertyChanged(nameof(SelectedGameCountText));
        OnPropertyChanged(nameof(RemoveSelectedGamesText));
        OnPropertyChanged(nameof(IsLibraryPage));
        OnPropertyChanged(nameof(IsRecentPage));
        OnPropertyChanged(nameof(IsNotRecentPage));
        OnPropertyChanged(nameof(CollectionActionText));
    }

    private void HandleRuntimeStatusChanged(object? sender, GameProcessStatusChangedEventArgs args)
    {
        _ = sender;
        if (_uiContext is null)
        {
            ApplyRuntimeStatus(args);
            return;
        }

        _uiContext.Post(static state =>
        {
            var update = (RuntimeUpdate)state!;
            update.ViewModel.ApplyRuntimeStatus(update.EventArgs);
        }, new RuntimeUpdate(this, args));
    }

    private void ApplyRuntimeStatus(GameProcessStatusChangedEventArgs args)
    {
        var card = Games.FirstOrDefault(game => game.Id == args.GameId);
        if (card is null)
        {
            return;
        }

        card.UpdateRuntime(args.Runtime);
        if (SelectedNavigationItem?.Label == "正在运行")
        {
            ApplyFilter();
        }

        if (args.State == GameRuntimeState.NotRunning)
        {
            _ = RefreshGameAfterSessionAsync(card.Id, _viewModelLifetime.Token);
        }
    }

    private async Task RefreshGameAfterSessionAsync(Guid gameId, CancellationToken cancellationToken)
    {
        try
        {
            var storedGames = await _gameLibraryService.GetGamesAsync(new(), cancellationToken);
            var stored = storedGames.FirstOrDefault(game => game.Id == gameId);
            var card = Games.FirstOrDefault(game => game.Id == gameId);
            if (stored is not null && card is not null)
            {
                card.Update(stored);
                ApplyFilter();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            GameOperationFailed(_logger, "刷新会话时长", exception);
        }
    }

    private async Task RefreshSessionClocksAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                _uiContext?.Post(
                    static state =>
                    {
                        var viewModel = (MainWindowViewModel)state!;
                        foreach (var game in viewModel.Games.Where(static game => game.IsSessionActive))
                        {
                            game.RefreshSessionClock();
                        }
                    },
                    this);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ChangeThemeAsync(
        ThemePreference preference,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _themeChangeGate.WaitAsync(cancellationToken);

        var previousTheme = CurrentTheme;
        try
        {
            CurrentTheme = preference;
            await _themePreferenceStore.SetAsync(preference, cancellationToken);
            StartupStatus = "外观设置已保存";
            StartupStatusGlyph = "\uE930";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CurrentTheme = previousTheme;
        }
        catch (Exception exception)
        {
            CurrentTheme = previousTheme;
            ThemePreferenceSaveFailed(_logger, exception);
            StartupStatus = "无法保存外观设置，请查看日志";
            StartupStatusGlyph = "\uE783";
        }
        finally
        {
            _themeChangeGate.Release();
        }
    }

    private async Task LoadOverlaySettingsAsync(CancellationToken cancellationToken)
    {
        _globalOverlayProfile = await _overlaySettingsService.GetGlobalAsync(cancellationToken);
        IsOverlayEnabled = _globalOverlayProfile.IsEnabled;
        SelectedOverlayPosition = OverlayPositionOptions.First(
            option => option.Value == _globalOverlayProfile.Position);
        OverlayScalePercent = _globalOverlayProfile.ScalePercent;
        OverlayOpacityPercent = _globalOverlayProfile.BackgroundOpacityPercent;
        ShowOverlayFps = _globalOverlayProfile.ShowFps;
        ShowOverlayCpu = _globalOverlayProfile.ShowCpu;
        ShowOverlayGpu = _globalOverlayProfile.ShowGpu;
        ShowOverlayRam = _globalOverlayProfile.ShowRam;
        OverlayToggleHotkey = _globalOverlayProfile.ToggleHotkey;
        HideOverlayWhenGameNotForeground = _globalOverlayProfile.HideWhenGameNotForeground;
        OverlaySettingsStatus = "覆盖层设置已从本地数据库加载。";
    }

    private OverlayProfileEditorInput CreateOverlayInput() =>
        new(
            IsOverlayEnabled,
            SelectedOverlayPosition.Value,
            OverlayScalePercent,
            checked((int)Math.Round(OverlayOpacityPercent)),
            ShowOverlayFps,
            ShowOverlayCpu,
            ShowOverlayGpu,
            ShowOverlayRam,
            OverlayToggleHotkey,
            HideOverlayWhenGameNotForeground);

    private void HandleOverlayRuntimeStatusChanged(object? sender, OverlayRuntimeStatusEventArgs args)
    {
        _ = sender;
        _uiContext?.Post(
            static state =>
            {
                var update = (OverlayRuntimeUpdate)state!;
                update.ViewModel.OverlayRuntimeStatusText = update.EventArgs.Status.Message;
            },
            new OverlayRuntimeUpdate(this, args));
    }

    private void ReportGameOperationFailure(string operationName, Exception exception)
    {
        GameOperationFailed(_logger, operationName, exception);
        StatusMessage = exception switch
        {
            FileNotFoundException => exception.Message,
            DirectoryNotFoundException => exception.Message,
            NotSupportedException => exception.Message,
            ArgumentException => exception.Message,
            InvalidOperationException => exception.Message,
            _ => $"{operationName}失败，请查看日志后重试。",
        };
    }

    private sealed record RuntimeUpdate(
        MainWindowViewModel ViewModel,
        GameProcessStatusChangedEventArgs EventArgs);

    private sealed record OverlayRuntimeUpdate(
        MainWindowViewModel ViewModel,
        OverlayRuntimeStatusEventArgs EventArgs);

    private sealed record StatusMessageClearRequest(
        MainWindowViewModel ViewModel,
        string Message);
}
