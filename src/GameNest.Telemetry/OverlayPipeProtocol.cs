using System.Buffers.Binary;
using System.Text.Json;
using GameNest.Application;

namespace GameNest.Telemetry;

public static class OverlayMessageTypes
{
    public const string Frame = "frame";
    public const string Hide = "hide";
    public const string Shutdown = "shutdown";
    public const string Ready = "ready";
    public const string Status = "status";
}

public sealed record OverlayWireMetric(
    double? Value,
    TelemetryMetricStatus Status,
    string? Detail);

public sealed record OverlayWireFrame(
    int Left,
    int Top,
    int Width,
    int Height,
    uint Dpi,
    bool IsVisible,
    string Position,
    int ScalePercent,
    int BackgroundOpacityPercent,
    bool ShowFps,
    bool ShowCpu,
    bool ShowGpu,
    bool ShowRam,
    string ToggleHotkey,
    OverlayWireMetric Fps,
    OverlayWireMetric Cpu,
    OverlayWireMetric Gpu,
    OverlayWireMetric Ram);

public sealed record OverlayWireStatus(
    string State,
    bool IsHotkeyAvailable,
    string Message);

public sealed record OverlayPipeMessage(
    int Version,
    string Type,
    OverlayWireFrame? Frame = null,
    OverlayWireStatus? Status = null)
{
    public const int CurrentVersion = 1;

    public static OverlayPipeMessage CreateFrame(OverlayFrame frame) =>
        new(
            CurrentVersion,
            OverlayMessageTypes.Frame,
            new OverlayWireFrame(
                frame.Window.ContentBounds.Left,
                frame.Window.ContentBounds.Top,
                frame.Window.ContentBounds.Width,
                frame.Window.ContentBounds.Height,
                frame.Window.Dpi,
                frame.IsVisible,
                frame.Profile.Position.ToString(),
                frame.Profile.ScalePercent,
                frame.Profile.BackgroundOpacityPercent,
                frame.Profile.ShowFps,
                frame.Profile.ShowCpu,
                frame.Profile.ShowGpu,
                frame.Profile.ShowRam,
                frame.Profile.ToggleHotkey,
                ToWire(frame.Snapshot.Fps),
                ToWire(frame.Snapshot.CpuPercent),
                ToWire(frame.Snapshot.GpuPercent),
                ToWire(frame.Snapshot.RamBytes)));

    public static OverlayPipeMessage CreateCommand(string type) => new(CurrentVersion, type);

    public static OverlayPipeMessage CreateStatus(OverlayWireStatus status, string type = OverlayMessageTypes.Status) =>
        new(CurrentVersion, type, Status: status);

    private static OverlayWireMetric ToWire(TelemetryMetric metric) =>
        new(metric.Value, metric.Status, metric.Detail);
}

public static class OverlayPipeProtocol
{
    public const int MaximumMessageBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(
        Stream stream,
        OverlayPipeMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length > MaximumMessageBytes)
        {
            throw new InvalidDataException("覆盖层消息超过 64KB 安全限制。");
        }

        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<OverlayPipeMessage?> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var prefix = new byte[sizeof(int)];
        if (!await ReadExactAsync(stream, prefix, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > MaximumMessageBytes)
        {
            throw new InvalidDataException("覆盖层消息长度无效。");
        }

        var payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false))
        {
            throw new EndOfStreamException("覆盖层消息在接收完成前中断。");
        }

        var message = JsonSerializer.Deserialize<OverlayPipeMessage>(payload, JsonOptions)
                      ?? throw new InvalidDataException("覆盖层消息内容为空。");
        if (message.Version != OverlayPipeMessage.CurrentVersion)
        {
            throw new InvalidDataException($"不支持的覆盖层协议版本：{message.Version}。");
        }

        return message;
    }

    private static async Task<bool> ReadExactAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }
}
