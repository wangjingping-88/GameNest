using System.Globalization;

namespace GameNest.Telemetry;

public sealed record GpuCounterSample(string InstanceName, double UtilizationPercent);

public static class GpuMetricAggregator
{
    public static double? Aggregate(
        IEnumerable<GpuCounterSample> samples,
        IReadOnlyCollection<int> targetProcessIds)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(targetProcessIds);
        if (targetProcessIds.Count == 0)
        {
            return null;
        }

        var targets = targetProcessIds.ToHashSet();
        var engines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in samples)
        {
            if (!double.IsFinite(sample.UtilizationPercent) ||
                !TryReadProcessId(sample.InstanceName, out var processId) ||
                !targets.Contains(processId))
            {
                continue;
            }

            var engineKey = RemoveProcessSegment(sample.InstanceName);
            engines[engineKey] = engines.GetValueOrDefault(engineKey) + Math.Max(0, sample.UtilizationPercent);
        }

        return engines.Count == 0 ? null : Math.Clamp(engines.Values.Max(), 0, 100);
    }

    private static bool TryReadProcessId(string instanceName, out int processId)
    {
        processId = 0;
        var marker = instanceName.IndexOf("pid_", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return false;
        }

        var start = marker + 4;
        var end = instanceName.IndexOf('_', start);
        var value = end < 0 ? instanceName.AsSpan(start) : instanceName.AsSpan(start, end - start);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out processId);
    }

    private static string RemoveProcessSegment(string instanceName)
    {
        var marker = instanceName.IndexOf("pid_", StringComparison.OrdinalIgnoreCase);
        var end = marker < 0 ? -1 : instanceName.IndexOf('_', marker + 4);
        return marker < 0 || end < 0
            ? instanceName
            : string.Concat(instanceName.AsSpan(0, marker), instanceName.AsSpan(end + 1));
    }
}
