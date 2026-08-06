namespace GameNest.App.ViewModels;

public enum SettingsSectionId
{
    PerformanceOverlay,
    Appearance,
    ApplicationUpdate,
    DataMaintenance,
    Compatibility,
}

public sealed record SettingsSectionItemViewModel(
    SettingsSectionId Id,
    string Label,
    string Description,
    string Glyph);
