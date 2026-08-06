namespace GameNest.Application;

/// <summary>
/// 以异步方式读取和保存界面主题偏好。
/// </summary>
public interface IThemePreferenceStore
{
    Task<ThemePreference> GetAsync(CancellationToken cancellationToken);

    Task SetAsync(ThemePreference preference, CancellationToken cancellationToken);
}
