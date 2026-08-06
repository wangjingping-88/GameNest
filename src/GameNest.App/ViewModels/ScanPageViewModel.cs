using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameNest.Application;
using GameNest.Domain;
using Microsoft.Extensions.Logging;

namespace GameNest.App.ViewModels;

public sealed partial class ScanPageViewModel(
    GameScanService scanService,
    ILogger<ScanPageViewModel> logger) : ObservableObject, IDisposable
{
    private static readonly Action<ILogger, string, Exception?> ScanOperationFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2200, nameof(ScanOperationFailed)),
            "扫描页面操作失败：{OperationName}。");

    private readonly ScanPauseController _pauseController = new();
    private CancellationTokenSource? _scanCancellation;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "等待开始扫描";

    [ObservableProperty]
    public partial string CurrentPath { get; set; } = "添加目录后可执行快速扫描或深度扫描";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartScan))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    public partial bool IsPaused { get; set; }

    public ObservableCollection<ScanRootViewModel> Roots { get; } = [];

    public ObservableCollection<ScanCandidateViewModel> HighConfidenceCandidates { get; } = [];

    public ObservableCollection<ScanCandidateViewModel> PossibleCandidates { get; } = [];

    public ObservableCollection<ScanCandidateViewModel> IgnoredCandidates { get; } = [];

    public bool HasRoots => Roots.Count > 0;

    public bool HasNoRoots => !HasRoots;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool CanStartScan => HasRoots && !IsScanning;

    public string PauseButtonText => IsPaused ? "继续" : "暂停";

    public string HighCountText => $"{HighConfidenceCandidates.Count} 项";

    public string PossibleCountText => $"{PossibleCandidates.Count} 项";

    public string IgnoredCountText => $"{IgnoredCandidates.Count} 项";

    public event EventHandler? CandidatesImported;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await ReloadRootsAsync(cancellationToken);
        await ReloadCandidatesAsync(cancellationToken);
    }

    public async Task AddRootAsync(
        string path,
        ScanMode mode,
        CancellationToken cancellationToken)
    {
        try
        {
            await scanService.AddRootAsync(path, mode, cancellationToken);
            await ReloadRootsAsync(cancellationToken);
            StatusMessage = $"已添加扫描目录：{path}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportFailure("添加扫描目录", "无法添加该目录，请确认磁盘在线且目录可访问。", exception);
        }
    }

    [RelayCommand]
    private Task StartQuickScanAsync(CancellationToken cancellationToken) =>
        RunScanAsync(ScanMode.Quick, cancellationToken);

    [RelayCommand]
    private Task StartDeepScanAsync(CancellationToken cancellationToken) =>
        RunScanAsync(ScanMode.Deep, cancellationToken);

    [RelayCommand]
    private void PauseOrResume()
    {
        if (!IsScanning)
        {
            return;
        }

        if (IsPaused)
        {
            _pauseController.Resume();
            IsPaused = false;
            StatusMessage = "扫描已继续。";
        }
        else
        {
            _pauseController.Pause();
            IsPaused = true;
            StatusMessage = "扫描已暂停；可以继续或取消。";
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _pauseController.Resume();
        _scanCancellation?.Cancel();
        StatusMessage = "正在取消扫描…";
    }

    [RelayCommand]
    private async Task ConfirmSelectedAsync(CancellationToken cancellationToken)
    {
        var selected = HighConfidenceCandidates
            .Concat(PossibleCandidates)
            .Concat(IgnoredCandidates)
            .Where(static candidate => candidate.IsSelected)
            .Select(static candidate => candidate.Id)
            .ToArray();
        if (selected.Length == 0)
        {
            StatusMessage = "请先勾选要导入的候选游戏。";
            return;
        }

        try
        {
            var imported = await scanService.ConfirmAsync(selected, cancellationToken);
            await ReloadCandidatesAsync(cancellationToken);
            StatusMessage = $"已将 {imported} 款游戏加入本地游戏库。";
            CandidatesImported?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportFailure("确认候选", "导入候选失败，请检查主程序是否仍然存在。", exception);
        }
    }

    [RelayCommand]
    private async Task ExcludeDirectoryAsync(
        ScanCandidateViewModel? candidate,
        CancellationToken cancellationToken)
    {
        if (candidate is null)
        {
            return;
        }

        try
        {
            var path = await scanService.ExcludeDirectoryAsync(candidate.Id, cancellationToken);
            await ReloadCandidatesAsync(cancellationToken);
            StatusMessage = $"已排除整个目录：{path}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportFailure("排除目录", "无法保存该排除规则。", exception);
        }
    }

    [RelayCommand]
    private async Task UndoExclusionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var path = await scanService.UndoLastExclusionAsync(cancellationToken);
            StatusMessage = path is null ? "当前没有可撤销的排除规则。" : $"已撤销排除：{path}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportFailure("撤销排除", "无法撤销最近的排除规则。", exception);
        }
    }

    [RelayCommand]
    private async Task RemoveRootAsync(
        ScanRootViewModel? root,
        CancellationToken cancellationToken)
    {
        if (root is null || IsScanning)
        {
            return;
        }

        try
        {
            await scanService.RemoveRootAsync(root.Id, cancellationToken);
            await ReloadRootsAsync(cancellationToken);
            StatusMessage = "已移除扫描目录；不会删除已导入的游戏。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportFailure("移除扫描目录", "无法移除该扫描目录。", exception);
        }
    }

    [RelayCommand]
    private async Task ToggleRootAsync(
        ScanRootViewModel? root,
        CancellationToken cancellationToken)
    {
        if (root is null || IsScanning)
        {
            return;
        }

        try
        {
            await scanService.SetRootEnabledAsync(root.Id, !root.IsEnabled, cancellationToken);
            await ReloadRootsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportFailure("更新扫描目录", "无法更新该目录的启用状态。", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _disposed = true;
    }

    private async Task RunScanAsync(ScanMode mode, CancellationToken commandCancellation)
    {
        if (IsScanning || !HasRoots)
        {
            StatusMessage = HasRoots ? "已有扫描正在运行。" : "请先添加扫描目录。";
            return;
        }

        _scanCancellation?.Dispose();
        _scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(commandCancellation);
        _pauseController.Resume();
        IsPaused = false;
        IsScanning = true;
        StatusMessage = null;
        ProgressText = mode == ScanMode.Quick ? "正在执行快速扫描" : "正在执行深度扫描";
        var progress = new Progress<GameScanProgress>(
            value =>
            {
                CurrentPath = string.IsNullOrWhiteSpace(value.CurrentPath) ? value.Stage : value.CurrentPath;
                ProgressText = $"{value.Stage} · 已检查 {value.CheckedDirectoryCount} 个目录 · 发现 {value.CandidateCount} 项 · {value.Elapsed:mm\\:ss}";
            });

        try
        {
            var summary = await scanService.RunAsync(
                mode,
                _pauseController,
                progress,
                _scanCancellation.Token);
            await ReloadRootsAsync(CancellationToken.None);
            await ReloadCandidatesAsync(CancellationToken.None);
            StatusMessage = summary.WasCancelled
                ? "扫描已取消，已完成的目录不会导致界面失去响应。"
                : $"扫描完成：发现 {summary.CandidateCount} 项候选，用时 {summary.Elapsed:mm\\:ss}。";
        }
        catch (Exception exception)
        {
            ReportFailure("运行扫描", "扫描未完成；已跳过受限目录，详细原因已写入本地日志。", exception);
        }
        finally
        {
            IsScanning = false;
            IsPaused = false;
            OnPropertyChanged(nameof(CanStartScan));
        }
    }

    private async Task ReloadRootsAsync(CancellationToken cancellationToken)
    {
        var roots = await scanService.GetRootsAsync(cancellationToken);
        Roots.Clear();
        foreach (var root in roots)
        {
            Roots.Add(new ScanRootViewModel(root));
        }

        OnPropertyChanged(nameof(HasRoots));
        OnPropertyChanged(nameof(HasNoRoots));
        OnPropertyChanged(nameof(CanStartScan));
    }

    private async Task ReloadCandidatesAsync(CancellationToken cancellationToken)
    {
        var candidates = await scanService.GetCandidatesAsync(cancellationToken);
        HighConfidenceCandidates.Clear();
        PossibleCandidates.Clear();
        IgnoredCandidates.Clear();
        foreach (var candidate in candidates.Where(static candidate =>
                     candidate.Decision != GameCandidateDecision.Confirmed))
        {
            var viewModel = new ScanCandidateViewModel(candidate);
            if (candidate.Decision == GameCandidateDecision.Excluded
                || candidate.Confidence == GameCandidateConfidence.Ignored)
            {
                IgnoredCandidates.Add(viewModel);
            }
            else if (candidate.Confidence == GameCandidateConfidence.High)
            {
                HighConfidenceCandidates.Add(viewModel);
            }
            else
            {
                PossibleCandidates.Add(viewModel);
            }
        }

        OnPropertyChanged(nameof(HighCountText));
        OnPropertyChanged(nameof(PossibleCountText));
        OnPropertyChanged(nameof(IgnoredCountText));
    }

    private void ReportFailure(string operation, string userMessage, Exception exception)
    {
        ScanOperationFailed(logger, operation, exception);
        StatusMessage = userMessage;
    }
}
