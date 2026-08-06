namespace GameNest.Telemetry;

/// <summary>
/// 标记本地性能采集与覆盖层协议程序集边界。
/// </summary>
public static class TelemetryAssembly
{
    public static System.Reflection.Assembly Instance => typeof(TelemetryAssembly).Assembly;
}
