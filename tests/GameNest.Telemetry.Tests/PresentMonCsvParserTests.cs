using GameNest.Telemetry;

namespace GameNest.Telemetry.Tests;

public sealed class PresentMonCsvParserTests
{
    [Fact]
    public void ParserUsesHeaderPositionsAndFiltersTargetProcess()
    {
        var parser = new PresentMonCsvParser();
        Assert.False(
            parser.TryRead(
                "Application,ProcessID,SwapChainAddress,CPUStartTime,DisplayedTime",
                42,
                out _));

        Assert.True(parser.TryRead("game.exe,42,0xABC,1234.5,1.250", 42, out var frame));
        Assert.Equal("0xABC", frame.SwapChain);
        Assert.Equal(1250, frame.TimestampMilliseconds);
        Assert.False(parser.TryRead("other.exe,43,0xABC,1300.0,1.300", 42, out _));
    }

    [Fact]
    public void ParserHandlesQuotedCsvFields()
    {
        var parser = new PresentMonCsvParser();
        _ = parser.TryRead(
            "Application,ProcessID,SwapChainAddress,CPUStartTime",
            42,
            out _);

        Assert.True(parser.TryRead("\"game, test.exe\",42,0x1,2500.5", 42, out var frame));
        Assert.Equal(2500.5, frame.TimestampMilliseconds);
    }

    [Fact]
    public void ParserHandlesPresentMonV1QpcTimeOutput()
    {
        var parser = new PresentMonCsvParser();
        Assert.False(
            parser.TryRead(
                "Application,ProcessID,SwapChainAddress,TimeInSeconds,msBetweenPresents,QPCTime",
                14060,
                out _));

        Assert.True(
            parser.TryRead(
                "Hollow Knight.exe,14060,0x0000015E1837C040,0.0075147,2.3061,3318.3805904",
                14060,
                out var frame));
        Assert.Equal("0x0000015E1837C040", frame.SwapChain);
        Assert.Equal(3318.3805904, frame.TimestampMilliseconds);
        Assert.Equal(2.3061, frame.MillisecondsBetweenPresents);
    }

    [Fact]
    public void ParserPrefersQpcTimeWhenPresentMonProvidesMultipleTimeColumns()
    {
        var parser = new PresentMonCsvParser();
        _ = parser.TryRead(
            "Application,ProcessID,SwapChainAddress,DisplayedTime,QPCTime",
            42,
            out _);

        Assert.True(parser.TryRead("game.exe,42,0x1,0.0001,1500.25", 42, out var frame));
        Assert.Equal(1500.25, frame.TimestampMilliseconds);
    }
}
