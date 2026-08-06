using System.Diagnostics;
using GameNest.Application;

namespace GameNest.Telemetry;

public sealed record ProcessMetricSample(TelemetryMetric CpuPercent, TelemetryMetric RamBytes);

public sealed class ProcessMetricSampler
{
    private readonly Dictionary<int, TimeSpan> _previousCpuTimes = [];
    private DateTimeOffset? _previousSampleAtUtc;

    public Task<ProcessMetricSample> SampleAsync(
        IReadOnlyList<int> processIds,
        CancellationToken cancellationToken) =>
        Task.Run(() => Sample(processIds, DateTimeOffset.UtcNow), cancellationToken);

    private ProcessMetricSample Sample(IReadOnlyList<int> processIds, DateTimeOffset capturedAtUtc)
    {
        var currentCpuTimes = new Dictionary<int, TimeSpan>();
        long privateBytes = 0;
        foreach (var processId in processIds.Distinct())
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    continue;
                }

                currentCpuTimes[processId] = process.TotalProcessorTime;
                privateBytes = checked(privateBytes + process.PrivateMemorySize64);
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            catch (OverflowException)
            {
                privateBytes = long.MaxValue;
            }
        }

        if (currentCpuTimes.Count == 0)
        {
            _previousCpuTimes.Clear();
            _previousSampleAtUtc = null;
            return new ProcessMetricSample(
                TelemetryMetric.Unavailable("已确认的游戏进程已退出。", TelemetryMetricStatus.TargetExited),
                TelemetryMetric.Unavailable("已确认的游戏进程已退出。", TelemetryMetricStatus.TargetExited));
        }

        var ram = TelemetryMetric.Available(privateBytes);
        TelemetryMetric cpu;
        if (_previousSampleAtUtc is null)
        {
            cpu = TelemetryMetric.Starting("正在建立 CPU 采样基线。");
        }
        else
        {
            var wallSeconds = (capturedAtUtc - _previousSampleAtUtc.Value).TotalSeconds;
            var cpuSeconds = currentCpuTimes.Sum(
                pair =>
                    _previousCpuTimes.TryGetValue(pair.Key, out var previous)
                        ? Math.Max(0, (pair.Value - previous).TotalSeconds)
                        : 0);
            cpu = wallSeconds <= 0
                ? TelemetryMetric.Starting("正在建立 CPU 采样基线。")
                : TelemetryMetric.Available(
                    NormalizeCpuPercent(cpuSeconds, wallSeconds, Environment.ProcessorCount));
        }

        _previousCpuTimes.Clear();
        foreach (var pair in currentCpuTimes)
        {
            _previousCpuTimes.Add(pair.Key, pair.Value);
        }

        _previousSampleAtUtc = capturedAtUtc;
        return new ProcessMetricSample(cpu, ram);
    }

    public static double NormalizeCpuPercent(
        double processorSeconds,
        double wallSeconds,
        int logicalProcessorCount)
    {
        if (!double.IsFinite(processorSeconds) || processorSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processorSeconds));
        }

        if (!double.IsFinite(wallSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(wallSeconds));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wallSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(logicalProcessorCount);

        return Math.Clamp(processorSeconds / wallSeconds / logicalProcessorCount * 100d, 0, 100);
    }
}
