namespace GameNest.Application;

/// <summary>
/// 初始化应用所需的本地数据结构。
/// </summary>
public interface IApplicationDataInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
