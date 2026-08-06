using System.Diagnostics;
using System.IO.Pipes;
using GameNest.Application;
using GameNest.Domain;
using Microsoft.Extensions.Logging;

namespace GameNest.Telemetry;

public sealed class WindowsOverlayController : IOverlayController, IAsyncDisposable
{
    private static readonly Action<ILogger, int, Exception?> OverlayStarted =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(3050, nameof(OverlayStarted)),
            "GameNest.Overlay 已启动，PID 为 {ProcessId}。");

    private static readonly Action<ILogger, string, Exception?> OverlayFaulted =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(3051, nameof(OverlayFaulted)),
            "GameNest.Overlay 连接失败：{Reason}");

    private readonly OverlayProcessOptions _options;
    private readonly ILogger<WindowsOverlayController> _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _connectionLifetime;
    private NamedPipeServerStream? _pipe;
    private Process? _process;
    private Task? _readerTask;
    private OverlayHotkey? _configuredHotkey;
    private OverlayControllerStatus _status =
        new(OverlayControllerState.Stopped, true, "覆盖层尚未启动。");
    private bool _disposed;

    public WindowsOverlayController(
        OverlayProcessOptions options,
        ILogger<WindowsOverlayController> logger)
    {
        _options = options;
        _logger = logger;
    }

    public event EventHandler<OverlayControllerStatusEventArgs>? StatusChanged;

    public OverlayControllerStatus Status
    {
        get
        {
            lock (_syncRoot)
            {
                return _status;
            }
        }
    }

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Status.State == OverlayControllerState.Ready &&
                _pipe?.IsConnected == true &&
                _process is { HasExited: false })
            {
                return;
            }

            await ShutdownCoreAsync(CancellationToken.None).ConfigureAwait(false);
            if (!File.Exists(_options.ExecutablePath))
            {
                var message = $"未找到独立覆盖层程序：{_options.ExecutablePath}";
                SetStatus(new OverlayControllerStatus(OverlayControllerState.Faulted, true, message));
                throw new FileNotFoundException(message, _options.ExecutablePath);
            }

            SetStatus(new OverlayControllerStatus(OverlayControllerState.Starting, true, "正在启动独立覆盖层进程。"));
            var pipeName = $"GameNest.Overlay.{Environment.ProcessId}.{Guid.NewGuid():N}";
            var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var lifetime = new CancellationTokenSource();
            var ready = new TaskCompletionSource<OverlayControllerStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.ExecutablePath,
                Arguments = $"--pipe {pipeName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_options.ExecutablePath) ?? AppContext.BaseDirectory,
            };

            Process? process = null;
            try
            {
                process = Process.Start(startInfo) ?? throw new InvalidOperationException("覆盖层进程未能启动。");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.ConnectionTimeout);
                await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
                lock (_syncRoot)
                {
                    _pipe = pipe;
                    _process = process;
                    _connectionLifetime = lifetime;
                    _readerTask = ReadStatusesAsync(pipe, ready, lifetime.Token);
                }

                var readyStatus = await ready.Task.WaitAsync(_options.ConnectionTimeout, cancellationToken).ConfigureAwait(false);
                if (readyStatus.State != OverlayControllerState.Ready)
                {
                    throw new IOException($"覆盖层未完成就绪握手：{readyStatus.Message}");
                }

                SetStatus(readyStatus);
                OverlayStarted(_logger, process.Id, null);
            }
            catch (Exception exception)
            {
                lock (_syncRoot)
                {
                    if (ReferenceEquals(_pipe, pipe))
                    {
                        _pipe = null;
                        _process = null;
                        _connectionLifetime = null;
                        _readerTask = null;
                        _configuredHotkey = null;
                    }
                }

                pipe.Dispose();
                lifetime.Cancel();
                lifetime.Dispose();
                if (process is { HasExited: false })
                {
                    process.Kill(entireProcessTree: true);
                }

                process?.Dispose();
                OverlayFaulted(_logger, exception.Message, exception);
                SetStatus(
                    new OverlayControllerStatus(
                        OverlayControllerState.Faulted,
                        true,
                        "覆盖层进程无法连接，游戏和遥测仍可继续运行。"));
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task UpdateAsync(OverlayFrame frame, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        _configuredHotkey = OverlayHotkey.Parse(frame.Profile.ToggleHotkey);
        await WriteAsync(OverlayPipeMessage.CreateFrame(frame), cancellationToken).ConfigureAwait(false);
    }

    public Task HideAsync(CancellationToken cancellationToken) =>
        WriteIfConnectedAsync(OverlayPipeMessage.CreateCommand(OverlayMessageTypes.Hide), cancellationToken);

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ShutdownCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task<bool> IsHotkeyAvailableAsync(
        OverlayHotkey hotkey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        if (_configuredHotkey?.DisplayText.Equals(hotkey.DisplayText, StringComparison.OrdinalIgnoreCase) == true &&
            Status.State == OverlayControllerState.Ready)
        {
            return Task.FromResult(Status.IsHotkeyAvailable);
        }

        return WindowsHotkeyProbe.IsAvailableAsync(hotkey, cancellationToken);
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
            await ShutdownCoreAsync(CancellationToken.None).ConfigureAwait(false);
            _disposed = true;
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
            _writeGate.Dispose();
        }
    }

    private async Task ReadStatusesAsync(
        NamedPipeServerStream pipe,
        TaskCompletionSource<OverlayControllerStatus> ready,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && pipe.IsConnected)
            {
                var message = await OverlayPipeProtocol.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                if (message.Status is null)
                {
                    continue;
                }

                var status = new OverlayControllerStatus(
                    message.Type == OverlayMessageTypes.Ready
                        ? OverlayControllerState.Ready
                        : Enum.TryParse<OverlayControllerState>(message.Status.State, true, out var parsed)
                            ? parsed
                            : OverlayControllerState.Ready,
                    message.Status.IsHotkeyAvailable,
                    message.Status.Message);
                if (message.Type == OverlayMessageTypes.Ready)
                {
                    _ = ready.TrySetResult(status);
                }

                SetStatus(status);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                var disconnected = new OverlayControllerStatus(
                    OverlayControllerState.Disconnected,
                    true,
                    "覆盖层连接已断开；游戏和主程序未受影响。");
                _ = ready.TrySetResult(disconnected);
                SetStatus(disconnected);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _ = ready.TrySetException(exception);
            OverlayFaulted(_logger, exception.Message, exception);
            SetStatus(
                new OverlayControllerStatus(
                    OverlayControllerState.Faulted,
                    true,
                    "覆盖层管道消息无效或连接异常。"));
        }
    }

    private Task WriteIfConnectedAsync(OverlayPipeMessage message, CancellationToken cancellationToken) =>
        _pipe?.IsConnected == true ? WriteAsync(message, cancellationToken) : Task.CompletedTask;

    private async Task WriteAsync(OverlayPipeMessage message, CancellationToken cancellationToken)
    {
        var pipe = _pipe;
        if (pipe?.IsConnected != true)
        {
            throw new IOException("覆盖层管道尚未连接。");
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await OverlayPipeProtocol.WriteAsync(pipe, message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ShutdownCoreAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? lifetime;
        NamedPipeServerStream? pipe;
        Process? process;
        Task? readerTask;
        lock (_syncRoot)
        {
            lifetime = _connectionLifetime;
            pipe = _pipe;
            process = _process;
            readerTask = _readerTask;
            _connectionLifetime = null;
            _pipe = null;
            _process = null;
            _readerTask = null;
            _configuredHotkey = null;
        }

        if (pipe?.IsConnected == true)
        {
            try
            {
                await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await OverlayPipeProtocol
                        .WriteAsync(pipe, OverlayPipeMessage.CreateCommand(OverlayMessageTypes.Shutdown), cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _writeGate.Release();
                }
            }
            catch (IOException)
            {
            }
        }

        lifetime?.Cancel();
        if (process is { HasExited: false })
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        if (readerTask is not null)
        {
            try
            {
                await readerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        pipe?.Dispose();
        process?.Dispose();
        lifetime?.Dispose();
        SetStatus(new OverlayControllerStatus(OverlayControllerState.Stopped, true, "覆盖层已停止。"));
    }

    private void SetStatus(OverlayControllerStatus status)
    {
        lock (_syncRoot)
        {
            _status = status;
        }

        StatusChanged?.Invoke(this, new OverlayControllerStatusEventArgs(status));
    }
}
