using GameNest.Telemetry;

namespace GameNest.Telemetry.Tests;

public sealed class ProcessMetricSamplerTests
{
    [Theory]
    [InlineData(1, 1, 1, 100)]
    [InlineData(1, 1, 4, 25)]
    [InlineData(8, 1, 8, 100)]
    [InlineData(10, 1, 4, 100)]
    public void CpuIsNormalizedToLogicalProcessorCountAndClamped(
        double processorSeconds,
        double wallSeconds,
        int processorCount,
        double expected)
    {
        Assert.Equal(
            expected,
            ProcessMetricSampler.NormalizeCpuPercent(processorSeconds, wallSeconds, processorCount));
    }
}
