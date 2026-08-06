using System.Diagnostics;
using GameNest.Application;
using Microsoft.Extensions.Logging;

namespace GameNest.Telemetry;

internal sealed class PresentMonFpsProvider(
    PresentMonOptions options,
    ILogger<PresentMonFpsProvider> logger) : IAsyncDisposable
{
    private static readonly Action<ILogger, int, Exception?> CaptureStarted =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(3000, nameof(CaptureStarted)),
            "PresentMon 已开始采集 PID {ProcessId}。");

    private static readonly Action<ILogger, int, string, Exception?> CaptureStopped =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(3001, nameof(CaptureStopped)),
            "PresentMon 已停止采集 PID {ProcessId}，状态为 {Status}。");

    private readonly object _syncRoot = new();
    private CancellationTokenSource? _captureLifetime;
    private Process? _process;
    private Task? _readerTask;
    private int _processId;
    private TelemetryMetric _current = TelemetryMetric.Starting("正在等待首个呈现事件。");

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

    public async Task StartAsync(int processId, CancellationToken cancellationToken)
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        if (!File.Exists(options.ExecutablePath))
        {
            SetCurrent(
                TelemetryMetric.Unavailable(
                    "未找到固定版本的 PresentMon。",
                    TelemetryMetricStatus.NotSupported));
            return;
        }

        if (!await options.VerifyHashAsync(cancellationToken).ConfigureAwait(false))
        {
            SetCurrent(
                TelemetryMetric.Unavailable(
                    "PresentMon 文件哈希不匹配，已拒绝启动 FPS 采集。",
                    TelemetryMetricStatus.NotSupported));
            return;
        }

        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sessionName = $"GameNest-{processId}-{Guid.NewGuid():N}";
        var startInfo = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            Arguments =
                $"--process_id {processId} --output_stdout --no_console_stats --v1_metrics " +
                $"--qpc_time_ms --terminate_on_proc_exit --session_name {sessionName}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(options.ExecutablePath) ?? AppContext.BaseDirectory,
        };

        Process? process = null;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("PresentMon 未能启动。");
            lock (_syncRoot)
            {
                _processId = processId;
                _process = process;
                _captureLifetime = lifetime;
                _current = TelemetryMetric.Starting("正在等待首个呈现事件。");
                _readerTask = ReadAsync(process, processId, lifetime.Token);
            }

            CaptureStarted(logger, processId, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            process?.Dispose();
            lifetime.Dispose();
            SetCurrent(ClassifyFailure(exception.Message));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? lifetime;
        Process? process;
        Task? readerTask;
        int processId;
        lock (_syncRoot)
        {
            lifetime = _captureLifetime;
            process = _process;
            readerTask = _readerTask;
            processId = _processId;
            _captureLifetime = null;
            _process = null;
            _readerTask = null;
            _processId = 0;
        }

        if (lifetime is null)
        {
            return;
        }

        lifetime.Cancel();
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }

            if (readerTask is not null)
            {
                await readerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            CaptureStopped(logger, processId, Current.Status.ToString(), null);
            process?.Dispose();
            lifetime.Dispose();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);

    private async Task ReadAsync(Process process, int targetProcessId, CancellationToken cancellationToken)
    {
        var parser = new PresentMonCsvParser();
        var aggregator = new FpsRollingAggregator(TimeSpan.FromSeconds(1));
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (parser.TryRead(line, targetProcessId, out var frame))
                {
                    var fps = aggregator.Add(frame.SwapChain, frame.TimestampMilliseconds);
                    if (fps is not null)
                    {
                        SetCurrent(TelemetryMetric.Available(Math.Clamp(fps.Value, 0, 10000)));
                    }
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (!cancellationToken.IsCancellationRequested && Current.Status != TelemetryMetricStatus.Available)
            {
                SetCurrent(
                    string.IsNullOrWhiteSpace(error)
                        ? TelemetryMetric.Unavailable("目标进程没有可用的呈现事件。")
                        : ClassifyFailure(error));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetCurrent(ClassifyFailure(exception.Message));
        }
    }

    private static TelemetryMetric ClassifyFailure(string message)
    {
        var normalized = string.IsNullOrWhiteSpace(message) ? "PresentMon 采集失败。" : message.Trim();
        var permissionDenied = normalized.Contains("access", StringComparison.OrdinalIgnoreCase) ||
                               normalized.Contains("privilege", StringComparison.OrdinalIgnoreCase) ||
                               normalized.Contains("administrator", StringComparison.OrdinalIgnoreCase) ||
                               normalized.Contains("权限", StringComparison.OrdinalIgnoreCase);
        return TelemetryMetric.Unavailable(
            permissionDenied
                ? "普通权限无法启动 FPS ETW 会话；GameNest 不会自动提权。"
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
}
