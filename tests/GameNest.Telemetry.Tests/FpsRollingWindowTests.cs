using GameNest.Telemetry;

namespace GameNest.Telemetry.Tests;

public sealed class FpsRollingWindowTests
{
    [Fact]
    public void IntervalWindowUsesPresentMonFrameIntervals()
    {
        var window = new FpsIntervalRollingWindow(TimeSpan.FromSeconds(1));

        var first = window.Add(1000d / 60d);
        var second = window.Add(1000d / 60d);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(60, first.Value, precision: 6);
        Assert.Equal(60, second.Value, precision: 6);
    }

    [Fact]
    public void OneSecondWindowCalculatesFrameRateAndPrunesOldFrames()
    {
        var window = new FpsRollingWindow(TimeSpan.FromSeconds(1));

        for (var index = 0; index <= 60; index++)
        {
            _ = window.Add(index * (1000d / 60d));
        }

        Assert.InRange(window.Current!.Value, 59.9, 60.1);
        _ = window.Add(2001);
        Assert.Null(window.Current);
    }

    [Fact]
    public void AggregatorUsesBusiestSwapChainInsteadOfAddingAllSwapChains()
    {
        var aggregator = new FpsRollingAggregator(TimeSpan.FromSeconds(1));
        for (var index = 0; index <= 60; index++)
        {
            _ = aggregator.Add("main", index * (1000d / 60d));
        }

        for (var index = 0; index <= 30; index++)
        {
            _ = aggregator.Add("video", index * (1000d / 30d));
        }

        Assert.InRange(aggregator.Current!.Value, 59.9, 60.1);
    }

    [Fact]
    public void AggregatorExpiresInactiveSwapChains()
    {
        var aggregator = new FpsRollingAggregator(TimeSpan.FromSeconds(1));
        for (var index = 0; index <= 60; index++)
        {
            _ = aggregator.Add("stale", index * (1000d / 60d));
        }

        _ = aggregator.Add("active", 2000);
        _ = aggregator.Add("active", 2033.333);

        Assert.InRange(aggregator.Current!.Value, 29.9, 30.1);
    }

    [Fact]
    public void OutOfOrderTimestampResetsTheWindow()
    {
        var window = new FpsRollingWindow(TimeSpan.FromSeconds(1));
        _ = window.Add(100);
        _ = window.Add(200);

        _ = window.Add(150);

        Assert.Null(window.Current);
    }
}
