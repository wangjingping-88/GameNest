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
    private readonly TimeSpan _window = window;
    private readonly Dictionary<string, FpsRollingWindow> _timestampWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FpsIntervalRollingWindow> _intervalWindows = new(StringComparer.OrdinalIgnoreCase);

    public double? Add(
        string swapChain,
        double timestampMilliseconds,
        double? millisecondsBetweenPresents = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(swapChain);
        if (millisecondsBetweenPresents is > 0d and <= 2000d)
        {
            if (!_intervalWindows.TryGetValue(swapChain, out var intervalWindow))
            {
                intervalWindow = new FpsIntervalRollingWindow(_window);
                _intervalWindows.Add(swapChain, intervalWindow);
            }

            return intervalWindow.Add(millisecondsBetweenPresents.Value);
        }

        foreach (var existingWindow in _timestampWindows.Values)
        {
            _ = existingWindow.Advance(timestampMilliseconds);
        }

        if (!_timestampWindows.TryGetValue(swapChain, out var fpsWindow))
        {
            fpsWindow = new FpsRollingWindow(_window);
            _timestampWindows.Add(swapChain, fpsWindow);
        }

        _ = fpsWindow.Add(timestampMilliseconds);
        return Current;
    }

    public double? Current => _intervalWindows.Values
        .Select(static item => item.Current)
        .Concat(_timestampWindows.Values.Select(static item => item.Current))
        .Where(static value => value is not null)
        .DefaultIfEmpty()
        .Max();
}

public sealed class FpsIntervalRollingWindow(TimeSpan window)
{
    private readonly Queue<double> _intervalsMilliseconds = new();
    private readonly double _windowMilliseconds = ValidateWindow(window).TotalMilliseconds;
    private double _totalMilliseconds;

    public double? Add(double millisecondsBetweenPresents)
    {
        if (!double.IsFinite(millisecondsBetweenPresents) || millisecondsBetweenPresents <= 0d)
        {
            return Current;
        }

        _intervalsMilliseconds.Enqueue(millisecondsBetweenPresents);
        _totalMilliseconds += millisecondsBetweenPresents;
        while (_totalMilliseconds > _windowMilliseconds && _intervalsMilliseconds.TryDequeue(out var oldest))
        {
            _totalMilliseconds -= oldest;
        }

        return Current;
    }

    public double? Current => _totalMilliseconds <= 0d
        ? null
        : _intervalsMilliseconds.Count * 1000d / _totalMilliseconds;

    private static TimeSpan ValidateWindow(TimeSpan value) =>
        value <= TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(nameof(value))
            : value;
}
