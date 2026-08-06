using GameNest.Domain;

namespace GameNest.Application;

public sealed record LocalGameFileInspection(
    string SourcePath,
    string ExecutablePath,
    string SuggestedTitle,
    string? Arguments,
    string WorkingDirectory,
    GameSourceType SourceType,
    LaunchKind LaunchKind,
    string IconSourcePath);
