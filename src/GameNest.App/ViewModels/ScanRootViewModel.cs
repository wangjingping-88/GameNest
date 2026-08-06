using GameNest.Domain;

namespace GameNest.App.ViewModels;

public sealed class ScanRootViewModel(ScanRoot model)
{
    public ScanRoot Model { get; } = model;

    public Guid Id => Model.Id;

    public string Path => Model.CurrentPath;

    public bool IsEnabled => Model.IsEnabled;

    public string ModeText => Model.ScanMode == ScanMode.Quick ? "快速" : "深度";

    public string StatusText => Model.IsOnline ? "磁盘在线" : "磁盘未连接";

    public string LastScanText => Model.LastScanUtc is null
        ? "尚未扫描"
        : $"上次扫描 {Model.LastScanUtc.Value.ToLocalTime():MM-dd HH:mm}";

    public string ToggleLabel => Model.IsEnabled ? "停用" : "启用";
}
