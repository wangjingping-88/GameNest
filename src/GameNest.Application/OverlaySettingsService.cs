using GameNest.Domain;

namespace GameNest.Application;

public sealed record OverlayProfileEditorInput(
    bool IsEnabled,
    OverlayPosition Position,
    int ScalePercent,
    int BackgroundOpacityPercent,
    bool ShowFps,
    bool ShowCpu,
    bool ShowGpu,
    bool ShowRam,
    string ToggleHotkey,
    bool HideWhenGameNotForeground);

public sealed class OverlaySettingsService(
    IOverlayProfileRepository repository,
    IOverlayController overlayController,
    IPerformanceTelemetry performanceTelemetry)
{
    public Task<OverlayProfile> GetGlobalAsync(CancellationToken cancellationToken) =>
        repository.GetGlobalAsync(cancellationToken);

    public Task<OverlayProfile?> GetForGameAsync(Guid gameId, CancellationToken cancellationToken) =>
        repository.GetForGameAsync(gameId, cancellationToken);

    public async Task<OverlayProfile> GetResolvedAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        var global = await repository.GetGlobalAsync(cancellationToken).ConfigureAwait(false);
        var game = await repository.GetForGameAsync(gameId, cancellationToken).ConfigureAwait(false);
        return OverlayProfile.Resolve(global, game);
    }

    public async Task SaveAsync(OverlayProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var hotkey = OverlayHotkey.Parse(profile.ToggleHotkey);
        if (!await overlayController
                .IsHotkeyAvailableAsync(hotkey, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException("该全局快捷键已被其他程序占用，请更换后重试。");
        }

        await repository.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OverlayProfile> SaveGlobalAsync(
        OverlayProfileEditorInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var existing = await repository.GetGlobalAsync(cancellationToken).ConfigureAwait(false);
        var profile = CreateProfile(existing.Id, null, input);
        await SaveAsync(profile, cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public async Task<OverlayProfile> SaveForGameAsync(
        Guid gameId,
        OverlayProfileEditorInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var existing = await repository.GetForGameAsync(gameId, cancellationToken).ConfigureAwait(false);
        var profile = CreateProfile(existing?.Id ?? Guid.NewGuid(), gameId, input);
        await SaveAsync(profile, cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public Task RemoveForGameAsync(Guid gameId, CancellationToken cancellationToken) =>
        repository.RemoveForGameAsync(gameId, cancellationToken);

    public Task<TelemetryCapabilityReport> CheckCapabilityAsync(CancellationToken cancellationToken) =>
        performanceTelemetry.CheckCapabilityAsync(cancellationToken);

    private static OverlayProfile CreateProfile(
        Guid id,
        Guid? gameId,
        OverlayProfileEditorInput input) =>
        new(
            id,
            gameId,
            input.IsEnabled,
            input.Position,
            input.ScalePercent,
            input.BackgroundOpacityPercent,
            input.ShowFps,
            input.ShowCpu,
            input.ShowGpu,
            input.ShowRam,
            input.ToggleHotkey,
            input.HideWhenGameNotForeground,
            DateTimeOffset.UtcNow);
}
