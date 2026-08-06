using System.Diagnostics;
using GameNest.Application;
using Microsoft.Extensions.Logging;

namespace GameNest.Telemetry;

public sealed class WindowsPerformanceTelemetry : IPerformanceTelemetry, IAsyncDisposable
{
    private static readonly Action<ILogger, Guid, int, Exception?> TelemetryStarted =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Information,
            new EventId(3010, nameof(TelemetryStarted)),
            "游戏 {GameId} 已开始本地性能采集，主 PID 为 {ProcessId}。");

    private static readonly Action<ILogger, Guid, Exception?> TelemetryStopped =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(3011, nameof(TelemetryStopped)),
            "游戏 {GameId} 的本地性能采集已停止。");

    private static readonly Action<ILogger, Guid, Exception?> TelemetryLoopFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(3012, nameof(TelemetryLoopFailed)),
            "游戏 {GameId} 的性能采集循环发生错误。");

    private readonly PresentMonOptions _presentMonOptions;
    private readonly PresentMonFpsProvider _fpsProvider;
    private readonly ILogger<WindowsPerformanceTelemetry> _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _sessionLifetime;
    private Task? _sessionTask;
    private TelemetryTarget? _target;
    private PerformanceSnapshot? _current;
    private bool _disposed;

    public WindowsPerformanceTelemetry(
        PresentMonOptions presentMonOptions,
        ILoggerFactory loggerFactory,
        ILogger<WindowsPerformanceTelemetry> logger)
    {
        _presentMonOptions = presentMonOptions;
        _fpsProvider = new PresentMonFpsProvider(
            presentMonOptions,
            loggerFactory.CreateLogger<PresentMonFpsProvider>());
        _logger = logger;
    }

    public event EventHandler<PerformanceSnapshotEventArgs>? SnapshotAvailable;

    public PerformanceSnapshot? Current
    {
        get
        {
            lock (_syncRoot)
            {
                return _current;
            }
        }
    }

    public async Task<TelemetryCapabilityReport> CheckCapabilityAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var fps = await CheckPresentMonAsync(cancellationToken).ConfigureAwait(false);
        TelemetryCapability gpu;
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (PdhGpuSampler.TryCreate(out var sampler, out var gpuMessage))
        {
            sampler?.Dispose();
            gpu = new TelemetryCapability(TelemetryMetricStatus.Available, gpuMessage);
        }
        else
        {
            gpu = new TelemetryCapability(TelemetryMetricStatus.NotSupported, gpuMessage);
        }

        return new TelemetryCapabilityReport(
            fps,
            new TelemetryCapability(TelemetryMetricStatus.Available, "游戏进程组 CPU 采样可用。"),
            gpu,
            new TelemetryCapability(TelemetryMetricStatus.Available, "游戏进程组私有内存采样可用。"),
            _presentMonOptions.Version,
            _presentMonOptions.ExecutablePath);
    }

    public async Task StartAsync(TelemetryTarget target, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        if (target.GameId == Guid.Empty || target.PrimaryProcessId <= 0 || target.ConfirmedProcessIds.Count == 0)
        {
            throw new ArgumentException("遥测目标必须包含已确认的游戏进程。", nameof(target));
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var lifetime = new CancellationTokenSource();
            var initial = new PerformanceSnapshot(
                target.GameId,
                DateTimeOffset.UtcNow,
                TelemetryMetric.Starting("正在启动 FPS 采集。"),
                TelemetryMetric.Starting("正在建立 CPU 采样基线。"),
                TelemetryMetric.Starting("正在等待 GPU 性能计数器。"),
                TelemetryMetric.Starting("正在读取游戏进程内存。"));
            lock (_syncRoot)
            {
                _target = target;
                _sessionLifetime = lifetime;
                _current = initial;
            }

            await _fpsProvider.StartAsync(target.PrimaryProcessId, lifetime.Token).ConfigureAwait(false);
            lock (_syncRoot)
            {
                _sessionTask = RunSessionAsync(target, lifetime.Token);
            }

            Publish(initial);
            TelemetryStarted(_logger, target.GameId, target.PrimaryProcessId, null);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
            await _fpsProvider.DisposeAsync().ConfigureAwait(false);
            _disposed = true;
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private async Task RunSessionAsync(TelemetryTarget target, CancellationToken cancellationToken)
    {
        var processMetrics = new ProcessMetricSampler();
        var gpuAvailable = PdhGpuSampler.TryCreate(out var gpuSampler, out var gpuMessage);
        var cpu = TelemetryMetric.Starting("正在建立 CPU 采样基线。");
        var ram = TelemetryMetric.Starting("正在读取游戏进程内存。");
        var gpu = gpuAvailable
            ? TelemetryMetric.Starting("正在等待 GPU 性能计数器。")
            : TelemetryMetric.Unavailable(gpuMessage, TelemetryMetricStatus.NotSupported);
        var tick = 0;

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                tick++;
                if (tick % 2 == 0)
                {
                    try
                    {
                        var processSample = await processMetrics
                            .SampleAsync(target.ConfirmedProcessIds, cancellationToken)
                            .ConfigureAwait(false);
                        cpu = processSample.CpuPercent;
                        ram = processSample.RamBytes;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        TelemetryLoopFailed(_logger, target.GameId, exception);
                        cpu = TelemetryMetric.Unavailable("CPU 采样暂时不可用，FPS、GPU 与 RAM 继续运行。");
                        ram = TelemetryMetric.Unavailable("RAM 采样暂时不可用，FPS、CPU 与 GPU 继续运行。");
                    }

                    if (gpuSampler is not null)
                    {
                        try
                        {
                            gpu = await Task.Run(
                                    () => gpuSampler.Sample(target.ConfirmedProcessIds),
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            TelemetryLoopFailed(_logger, target.GameId, exception);
                            gpu = TelemetryMetric.Unavailable("GPU 采样暂时不可用，其他指标继续运行。");
                        }
                    }
                }

                var snapshot = new PerformanceSnapshot(
                    target.GameId,
                    DateTimeOffset.UtcNow,
                    _fpsProvider.Current,
                    cpu,
                    gpu,
                    ram);
                Publish(snapshot);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TelemetryLoopFailed(_logger, target.GameId, exception);
            var failed = new PerformanceSnapshot(
                target.GameId,
                DateTimeOffset.UtcNow,
                _fpsProvider.Current,
                cpu,
                TelemetryMetric.Unavailable("GPU 采样循环发生错误，其他指标继续运行。"),
                ram);
            Publish(failed);
        }
        finally
        {
            gpuSampler?.Dispose();
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? lifetime;
        Task? sessionTask;
        TelemetryTarget? target;
        lock (_syncRoot)
        {
            lifetime = _sessionLifetime;
            sessionTask = _sessionTask;
            target = _target;
            _sessionLifetime = null;
            _sessionTask = null;
            _target = null;
            _current = null;
        }

        if (lifetime is null)
        {
            return;
        }

        lifetime.Cancel();
        await _fpsProvider.StopAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (sessionTask is not null)
            {
                await sessionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            lifetime.Dispose();
            if (target is not null)
            {
                TelemetryStopped(_logger, target.GameId, null);
            }
        }
    }

    private async Task<TelemetryCapability> CheckPresentMonAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_presentMonOptions.ExecutablePath))
        {
            return new TelemetryCapability(
                TelemetryMetricStatus.NotSupported,
                $"未找到 PresentMon {_presentMonOptions.Version}。" );
        }

        if (!await _presentMonOptions.VerifyHashAsync(cancellationToken).ConfigureAwait(false))
        {
            return new TelemetryCapability(
                TelemetryMetricStatus.NotSupported,
                "PresentMon 文件哈希不匹配，已拒绝启动 FPS 采集。");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _presentMonOptions.ExecutablePath,
            Arguments =
                $"--process_id {Environment.ProcessId} --no_csv --no_console_stats --timed 1 " +
                $"--terminate_after_timed --session_name GameNest-Capability-{Guid.NewGuid():N}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_presentMonOptions.ExecutablePath) ?? AppContext.BaseDirectory,
        };
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new TelemetryCapability(TelemetryMetricStatus.Unavailable, "PresentMon 兼容性检测无法启动。");
        }

        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            _ = await errorTask.ConfigureAwait(false);
            _ = await outputTask.ConfigureAwait(false);
            return new TelemetryCapability(
                TelemetryMetricStatus.Unavailable,
                "PresentMon 普通权限兼容性检测超时，FPS 将独立降级为 --。");
        }

        var error = await errorTask.ConfigureAwait(false);
        _ = await outputTask.ConfigureAwait(false);
        if (process.ExitCode == 0)
        {
            return new TelemetryCapability(
                TelemetryMetricStatus.Available,
                $"PresentMon {_presentMonOptions.Version} 已通过 SHA-256 和普通权限 ETW 会话检测。");
        }

        var permissionDenied = error.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
                               error.Contains("privilege", StringComparison.OrdinalIgnoreCase) ||
                               error.Contains("administrator", StringComparison.OrdinalIgnoreCase);
        return new TelemetryCapability(
            permissionDenied ? TelemetryMetricStatus.PermissionDenied : TelemetryMetricStatus.Unavailable,
            permissionDenied
                ? "PresentMon 文件校验通过，但当前普通用户无法创建 FPS ETW 会话；游戏运行时 FPS 将显示 --，GameNest 不会自动提权。"
                : $"PresentMon 兼容性检测失败（退出码 {process.ExitCode}）。");
    }

    private void Publish(PerformanceSnapshot snapshot)
    {
        lock (_syncRoot)
        {
            if (_target?.GameId != snapshot.GameId)
            {
                return;
            }

            _current = snapshot;
        }

        SnapshotAvailable?.Invoke(this, new PerformanceSnapshotEventArgs(snapshot));
    }
}
