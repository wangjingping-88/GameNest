using System.Diagnostics;
using GameNest.Application;
using Microsoft.Extensions.Logging;

namespace GameNest.Telemetry;

/// <summary>
/// 从全部确认的游戏进程采集 FPS。启动器常会创建实际负责呈现的子进程，
/// 因此只绑定启动 PID 会让覆盖层持续没有帧数据。
/// </summary>
internal sealed class PresentMonFpsProvider(
    PresentMonOptions options,
    ILogger<PresentMonFpsProvider> logger) : IAsyncDisposable
{
    private const double MaximumPlausibleFps = 2000d;
    private static readonly Action<ILogger, int, Exception?> CaptureStarted =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(3000, nameof(CaptureStarted)),
            "已为 {ProcessCount} 个游戏进程启动 FPS 采集。");

    private static readonly Action<ILogger, string, Exception?> TraceSessionCleanupFailed =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(3001, nameof(TraceSessionCleanupFailed)),
            "无法清理 PresentMon ETW 会话 {SessionName}。");

    private readonly object _syncRoot = new();
    private CancellationTokenSource? _captureLifetime;
    private IReadOnlyList<CaptureSession> _sessions = [];
    private Task? _monitorTask;
    private TelemetryMetric _current = TelemetryMetric.Starting("正在等待第一个呈现事件。");

    public TelemetryMetric Current
    {
        get
        {
            lock (_syncRoot)
            {
                return _current;
            }
        }
    }

    public async Task StartAsync(IReadOnlyList<int> processIds, CancellationToken cancellationToken)
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        var targets = processIds.Where(static id => id > 0).Distinct().ToArray();
        if (targets.Length == 0)
        {
            SetCurrent(TelemetryMetric.Unavailable("没有可采集 FPS 的已确认游戏进程。"));
            return;
        }

        if (!File.Exists(options.ExecutablePath))
        {
            SetCurrent(TelemetryMetric.Unavailable("未找到固定版本的 PresentMon。", TelemetryMetricStatus.NotSupported));
            return;
        }

        if (!await options.VerifyHashAsync(cancellationToken).ConfigureAwait(false))
        {
            SetCurrent(TelemetryMetric.Unavailable("PresentMon 文件哈希不匹配，已拒绝启动 FPS 采集。", TelemetryMetricStatus.NotSupported));
            return;
        }

        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sessions = new List<CaptureSession>(targets.Length);
        try
        {
            foreach (var processId in targets)
            {
                var sessionName = $"GameNest-{processId}";
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = options.ExecutablePath,
                    Arguments = $"--process_id {processId} --output_stdout --no_console_stats --v1_metrics --qpc_time_ms --terminate_on_proc_exit --stop_existing_session --session_name {sessionName}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(options.ExecutablePath) ?? AppContext.BaseDirectory,
                });
                if (process is null)
                {
                    continue;
                }

                var session = new CaptureSession(processId, sessionName, process);
                session.ReaderTask = ReadAsync(session, lifetime.Token);
                sessions.Add(session);
            }

            if (sessions.Count == 0)
            {
                lifetime.Dispose();
                SetCurrent(TelemetryMetric.Unavailable("PresentMon 无法启动 FPS 采集进程。"));
                return;
            }

            lock (_syncRoot)
            {
                _captureLifetime = lifetime;
                _sessions = sessions;
                _current = TelemetryMetric.Starting("正在等待已确认游戏进程的呈现事件。");
                _monitorTask = MonitorAsync(sessions, lifetime.Token);
            }
            CaptureStarted(logger, sessions.Count, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            foreach (var session in sessions)
            {
                session.Process.Dispose();
                await TerminateTraceSessionAsync(session.SessionName).ConfigureAwait(false);
            }

            lifetime.Dispose();
            SetCurrent(ClassifyFailure(exception.Message));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? lifetime;
        IReadOnlyList<CaptureSession> sessions;
        Task? monitorTask;
        lock (_syncRoot)
        {
            lifetime = _captureLifetime;
            sessions = _sessions;
            monitorTask = _monitorTask;
            _captureLifetime = null;
            _sessions = [];
            _monitorTask = null;
        }

        if (lifetime is null)
        {
            return;
        }

        lifetime.Cancel();
        foreach (var session in sessions)
        {
            try
            {
                if (!session.Process.HasExited)
                {
                    session.Process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        try
        {
            if (monitorTask is not null)
            {
                await monitorTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var session in sessions)
            {
                session.Process.Dispose();
                await TerminateTraceSessionAsync(session.SessionName).ConfigureAwait(false);
            }

            lifetime.Dispose();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);

    private async Task MonitorAsync(IReadOnlyList<CaptureSession> sessions, CancellationToken cancellationToken)
    {
        await Task.WhenAll(sessions.Select(static session => session.ReaderTask!)).ConfigureAwait(false);
        if (!cancellationToken.IsCancellationRequested && Current.Status != TelemetryMetricStatus.Available)
        {
            var details = string.Join("；", sessions.Select(static session => session.Error).Where(static error => !string.IsNullOrWhiteSpace(error)));
            SetCurrent(string.IsNullOrWhiteSpace(details)
                ? TelemetryMetric.Unavailable("已确认的游戏进程没有产生可用的呈现事件。")
                : ClassifyFailure(details));
        }
    }

    private async Task ReadAsync(CaptureSession session, CancellationToken cancellationToken)
    {
        var parser = new PresentMonCsvParser();
        var aggregator = new FpsRollingAggregator(TimeSpan.FromSeconds(1));
        var errorTask = session.Process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await session.Process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (parser.TryRead(line, session.ProcessId, out var frame))
                {
                    var fps = aggregator.Add(
                        frame.SwapChain,
                        frame.TimestampMilliseconds,
                        frame.MillisecondsBetweenPresents);
                    if (fps is > 0d and <= MaximumPlausibleFps)
                    {
                        session.HasPresented = true;
                        SetCurrent(TelemetryMetric.Available(fps.Value));
                    }
                    else if (fps is > MaximumPlausibleFps)
                    {
                        SetCurrent(TelemetryMetric.Unavailable("FPS 时间戳异常，正在等待下一组有效呈现事件。"));
                    }
                }
            }

            await session.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            session.Error = await errorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            session.Error = exception.Message;
        }
    }

    private static TelemetryMetric ClassifyFailure(string message)
    {
        var normalized = string.IsNullOrWhiteSpace(message) ? "PresentMon 采集失败。" : message.Trim();
        var permissionDenied = normalized.Contains("access", StringComparison.OrdinalIgnoreCase)
                               || normalized.Contains("privilege", StringComparison.OrdinalIgnoreCase)
                               || normalized.Contains("administrator", StringComparison.OrdinalIgnoreCase)
                               || normalized.Contains("权限", StringComparison.OrdinalIgnoreCase);
        return TelemetryMetric.Unavailable(
            permissionDenied
                ? "当前用户无法创建 FPS ETW 会话；GameNest 不会自动提权。"
                : $"FPS 不可用：{normalized}",
            permissionDenied ? TelemetryMetricStatus.PermissionDenied : TelemetryMetricStatus.Unavailable);
    }

    private void SetCurrent(TelemetryMetric value)
    {
        lock (_syncRoot)
        {
            _current = value;
        }
    }

    private async Task TerminateTraceSessionAsync(string sessionName)
    {
        try
        {
            using var cleanup = Process.Start(new ProcessStartInfo
            {
                FileName = options.ExecutablePath,
                Arguments = $"--terminate_existing_session --session_name {sessionName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(options.ExecutablePath) ?? AppContext.BaseDirectory,
            });
            if (cleanup is not null)
            {
                await cleanup.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            TraceSessionCleanupFailed(logger, sessionName, exception);
        }
    }

    private sealed class CaptureSession(int processId, string sessionName, Process process)
    {
        public int ProcessId { get; } = processId;

        public string SessionName { get; } = sessionName;

        public Process Process { get; } = process;

        public Task? ReaderTask { get; set; }

        public string? Error { get; set; }

        public bool HasPresented { get; set; }
    }
}
