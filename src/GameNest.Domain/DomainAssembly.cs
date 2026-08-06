namespace GameNest.Domain;

/// <summary>
/// 为架构边界测试提供稳定的领域程序集入口。
/// </summary>
public static class DomainAssembly
{
    public static System.Reflection.Assembly Instance => typeof(DomainAssembly).Assembly;
}
