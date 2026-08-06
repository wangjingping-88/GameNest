using System.IO.Pipes;
using System.Threading.Channels;
using GameNest.Telemetry;

namespace GameNest.Overlay;

internal sealed class OverlayHost(string pipeName) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<OverlayPipeMessage> _outgoing = Channel.CreateBounded<OverlayPipeMessage>(
        new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private NamedPipeClientStream? _pipe;
    private bool _disposed;

    public int Run()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        _pipe.ConnectAsync(5000, _lifetime.Token).GetAwaiter().GetResult();

        using var window = new NativeOverlayWindow(SendStatus);
        var readerTask = ReadCommandsAsync(window, _pipe, _lifetime.Token);
        var writerTask = WriteStatusesAsync(_pipe, _lifetime.Token);
        _outgoing.Writer.TryWrite(
            OverlayPipeMessage.CreateStatus(
                new OverlayWireStatus("Ready", true, "覆盖层进程已连接。"),
                OverlayMessageTypes.Ready));

        var exitCode = NativeOverlayWindow.RunMessageLoop();
        _lifetime.Cancel();
        _outgoing.Writer.TryComplete();
        try
        {
            Task.WhenAll(readerTask, writerTask).Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException exception) when (
            exception.InnerExceptions.All(static item => item is OperationCanceledException or IOException))
        {
        }

        return exitCode;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _pipe?.Dispose();
        _lifetime.Dispose();
    }

    private void SendStatus(OverlayWireStatus status) =>
        _outgoing.Writer.TryWrite(OverlayPipeMessage.CreateStatus(status));

    private static async Task ReadCommandsAsync(
        NativeOverlayWindow window,
        Stream pipe,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await OverlayPipeProtocol.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                window.Post(message);
                if (message.Type == OverlayMessageTypes.Shutdown)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            window.RequestClose();
        }
    }

    private async Task WriteStatusesAsync(Stream pipe, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _outgoing.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await OverlayPipeProtocol.WriteAsync(pipe, message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
    }
}
