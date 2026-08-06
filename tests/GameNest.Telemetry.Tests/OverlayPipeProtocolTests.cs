using System.Buffers.Binary;
using GameNest.Application;
using GameNest.Domain;
using GameNest.Telemetry;

namespace GameNest.Telemetry.Tests;

public sealed class OverlayPipeProtocolTests
{
    [Fact]
    public async Task FrameRoundTripsThroughLengthLimitedProtocol()
    {
        var profile = OverlayProfile.CreateDefault();
        var snapshot = new PerformanceSnapshot(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            TelemetryMetric.Available(118),
            TelemetryMetric.Available(34),
            TelemetryMetric.Unavailable("GPU 不可用。"),
            TelemetryMetric.Available(6.8 * 1024 * 1024 * 1024));
        var frame = new OverlayFrame(
            new GameWindowSnapshot(
                100,
                new GameWindowBounds(10, 20, 1920, 1080),
                144,
                true,
                false,
                true),
            profile,
            snapshot,
            true);
        await using var stream = new MemoryStream();

        await OverlayPipeProtocol.WriteAsync(
            stream,
            OverlayPipeMessage.CreateFrame(frame),
            TestContext.Current.CancellationToken);
        stream.Position = 0;
        var message = await OverlayPipeProtocol.ReadAsync(stream, TestContext.Current.CancellationToken);

        Assert.Equal(OverlayMessageTypes.Frame, message?.Type);
        Assert.Equal(1920, message?.Frame?.Width);
        Assert.Equal(118, message?.Frame?.Fps.Value);
        Assert.Equal(TelemetryMetricStatus.Unavailable, message?.Frame?.Gpu.Status);
    }

    [Fact]
    public async Task ProtocolRejectsOversizedMessageBeforeAllocatingPayload()
    {
        await using var stream = new MemoryStream();
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, OverlayPipeProtocol.MaximumMessageBytes + 1);
        await stream.WriteAsync(prefix, TestContext.Current.CancellationToken);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(
            () => OverlayPipeProtocol.ReadAsync(stream, TestContext.Current.CancellationToken));
    }
}
