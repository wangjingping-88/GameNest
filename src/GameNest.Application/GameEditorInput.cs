namespace GameNest.Application;

public sealed record GameEditorInput(
    string? Title = null,
    string? Description = null,
    string? Arguments = null,
    string? WorkingDirectory = null);
