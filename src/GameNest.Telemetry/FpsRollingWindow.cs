namespace GameNest.Telemetry;

public sealed class FpsRollingWindow(TimeSpan window)
{
    private readonly Queue<double> _timestampsMilliseconds = new();
    private readonly double _windowMilliseconds = ValidateWindow(window).TotalMilliseconds;
    private double? _lastTimestampMilliseconds;

    public double? Add(double timestampMilliseconds)
    {
        if (!double.IsFinite(timestampMilliseconds))
        {
            return Current;
        }

        if (_lastTimestampMilliseconds is not null && timestampMilliseconds < _lastTimestampMilliseconds)
        {
            _timestampsMilliseconds.Clear();
        }

        Advance(timestampMilliseconds);
        _timestampsMilliseconds.Enqueue(timestampMilliseconds);
        _lastTimestampMilliseconds = timestampMilliseconds;

        return Current;
    }

    public double? Advance(double timestampMilliseconds)
    {
        if (!double.IsFinite(timestampMilliseconds))
        {
            return Current;
        }

        while (_timestampsMilliseconds.TryPeek(out var first) &&
               timestampMilliseconds - first > _windowMilliseconds)
        {
            _timestampsMilliseconds.Dequeue();
        }

        return Current;
    }

    public double? Current
    {
        get
        {
            if (_timestampsMilliseconds.Count < 2)
            {
                return null;
            }

            var first = _timestampsMilliseconds.Peek();
            var last = _timestampsMilliseconds.Last();
            var durationSeconds = (last - first) / 1000d;
            return durationSeconds <= 0
                ? null
                : (_timestampsMilliseconds.Count - 1) / durationSeconds;
        }
    }

    private static TimeSpan ValidateWindow(TimeSpan value) =>
        value <= TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(nameof(value))
            : value;
}

public sealed class FpsRollingAggregator(TimeSpan window)
{
    private readonly Dictionary<string, FpsRollingWindow> _windows = new(StringComparer.OrdinalIgnoreCase);

    public double? Add(string swapChain, double timestampMilliseconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(swapChain);
        foreach (var existingWindow in _windows.Values)
        {
            _ = existingWindow.Advance(timestampMilliseconds);
        }

        if (!_windows.TryGetValue(swapChain, out var fpsWindow))
        {
            fpsWindow = new FpsRollingWindow(window);
            _windows.Add(swapChain, fpsWindow);
        }

        _ = fpsWindow.Add(timestampMilliseconds);
        return Current;
    }

    public double? Current => _windows.Values
        .Select(static item => item.Current)
        .Where(static value => value is not null)
        .DefaultIfEmpty()
        .Max();
}
