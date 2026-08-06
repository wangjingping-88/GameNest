using GameNest.Application;
using GameNest.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Telemetry.Tests;

public sealed class WindowsPerformanceTelemetryTests
{
    [Fact]
    public async Task MissingFpsProviderDoesNotStopCpuOrRamSampling()
    {
        var missingPresentMon = new PresentMonOptions(
            Path.Combine(Path.GetTempPath(), $"missing-presentmon-{Guid.NewGuid():N}.exe"),
            PresentMonOptions.SupportedVersion,
            PresentMonOptions.SupportedSha256);
        await using var telemetry = new WindowsPerformanceTelemetry(
            missingPresentMon,
            NullLoggerFactory.Instance,
            NullLogger<WindowsPerformanceTelemetry>.Instance);
        var processId = Environment.ProcessId;
        var gameId = Guid.NewGuid();

        await telemetry.StartAsync(
            new TelemetryTarget(gameId, processId, [processId]),
            TestContext.Current.CancellationToken);

        PerformanceSnapshot? snapshot = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            snapshot = telemetry.Current;
            if (snapshot?.CpuPercent.Status == TelemetryMetricStatus.Available &&
                snapshot.RamBytes.Status == TelemetryMetricStatus.Available)
            {
                break;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.NotNull(snapshot);
        Assert.Equal(TelemetryMetricStatus.NotSupported, snapshot.Fps.Status);
        Assert.Equal(TelemetryMetricStatus.Available, snapshot.CpuPercent.Status);
        Assert.Equal(TelemetryMetricStatus.Available, snapshot.RamBytes.Status);
        Assert.True(snapshot.RamBytes.Value > 0);

        await telemetry.StopAsync(TestContext.Current.CancellationToken);
        Assert.Null(telemetry.Current);
    }
}
