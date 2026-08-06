using GameNest.Telemetry;

namespace GameNest.Telemetry.Tests;

public sealed class GpuMetricAggregatorTests
{
    [Fact]
    public void AggregatorFiltersPidsGroupsSameEngineAndUsesBusiestEngine()
    {
        GpuCounterSample[] samples =
        [
            new("pid_42_luid_0x0000_phys_0_eng_0_engtype_3D", 30),
            new("pid_43_luid_0x0000_phys_0_eng_0_engtype_3D", 25),
            new("pid_42_luid_0x0000_phys_0_eng_1_engtype_Copy", 10),
            new("pid_99_luid_0x0000_phys_0_eng_0_engtype_3D", 90),
        ];

        var value = GpuMetricAggregator.Aggregate(samples, new HashSet<int> { 42, 43 });

        Assert.Equal(55, value);
    }

    [Fact]
    public void AggregatorReturnsNullWhenDriverHasNoTargetPidInstances() =>
        Assert.Null(
            GpuMetricAggregator.Aggregate(
                [new GpuCounterSample("pid_99_luid_0x0_eng_0_engtype_3D", 80)],
                new HashSet<int> { 42 }));
}
