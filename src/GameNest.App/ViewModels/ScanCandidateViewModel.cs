using CommunityToolkit.Mvvm.ComponentModel;
using GameNest.Domain;

namespace GameNest.App.ViewModels;

public sealed partial class ScanCandidateViewModel(GameCandidate model) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; } = model.IsPrimary && model.Confidence == GameCandidateConfidence.High;

    public GameCandidate Model { get; } = model;

    public Guid Id => Model.Id;

    public string Title => Model.Title;

    public string ExecutablePath => Model.ExecutablePath;

    public string SourceGlyph => Model.Source switch
    {
        GameCandidateSource.Steam => "\uE7FC",
        GameCandidateSource.Shortcut => "\uE71B",
        _ => "\uECAA",
    };

    public string ScoreText => $"{Model.Score} 分";

    public string SourceText => Model.Source switch
    {
        GameCandidateSource.Steam => "Steam 清单",
        GameCandidateSource.Shortcut => "快捷方式",
        GameCandidateSource.GenericExecutable => "通用 EXE",
        _ => "本地来源",
    };

    public string ConfidenceText => Model.Confidence switch
    {
        GameCandidateConfidence.High => "确定是游戏",
        GameCandidateConfidence.Medium => "可能是游戏",
        _ => Model.Decision == GameCandidateDecision.Excluded ? "已排除" : "已忽略",
    };

    public string PrimaryText => Model.IsPrimary ? "主程序" : "同目录备选";

    public string EvidenceText => Model.Evidence.Count == 0
        ? "增量扫描：文件指纹未变化"
        : string.Join("；", Model.Evidence.Select(static evidence => evidence.Description));
}
