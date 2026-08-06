using System.Runtime.InteropServices;
using GameNest.Application;

namespace GameNest.Telemetry;

internal sealed class PdhGpuSampler : IDisposable
{
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhMoreData = 0x800007D2;
    private const uint ErrorSuccess = 0;
    private const uint PdhCstatusValidData = 0;
    private const uint PdhCstatusNewData = 1;
    private readonly nint _query;
    private readonly nint _counter;
    private bool _disposed;

    private PdhGpuSampler(nint query, nint counter)
    {
        _query = query;
        _counter = counter;
        _ = PdhCollectQueryData(_query);
    }

    public static bool TryCreate(out PdhGpuSampler? sampler, out string message)
    {
        sampler = null;
        message = string.Empty;
        var status = PdhOpenQueryW(null, 0, out var query);
        if (status != ErrorSuccess)
        {
            message = $"无法打开 Windows GPU 性能查询（0x{status:X8}）。";
            return false;
        }

        status = PdhAddEnglishCounterW(
            query,
            @"\GPU Engine(*)\Utilization Percentage",
            0,
            out var counter);
        if (status != ErrorSuccess)
        {
            _ = PdhCloseQuery(query);
            message = $"当前系统没有可用的 GPU Engine 性能计数器（0x{status:X8}）。";
            return false;
        }

        sampler = new PdhGpuSampler(query, counter);
        message = "GPU Engine 性能计数器可用。";
        return true;
    }

    public TelemetryMetric Sample(IReadOnlyCollection<int> processIds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var status = PdhCollectQueryData(_query);
        if (status != ErrorSuccess)
        {
            return TelemetryMetric.Unavailable($"GPU 采样失败（0x{status:X8}）。");
        }

        uint bufferSize = 0;
        uint itemCount = 0;
        status = PdhGetFormattedCounterArrayW(
            _counter,
            PdhFormatDouble,
            ref bufferSize,
            ref itemCount,
            nint.Zero);
        if (status != PdhMoreData || bufferSize == 0)
        {
            return TelemetryMetric.Unavailable("GPU 计数器暂时没有目标进程数据。");
        }

        var buffer = Marshal.AllocHGlobal(checked((int)bufferSize));
        try
        {
            status = PdhGetFormattedCounterArrayW(
                _counter,
                PdhFormatDouble,
                ref bufferSize,
                ref itemCount,
                buffer);
            if (status != ErrorSuccess)
            {
                return TelemetryMetric.Unavailable($"GPU 计数器读取失败（0x{status:X8}）。");
            }

            var samples = new List<GpuCounterSample>(checked((int)itemCount));
            var itemSize = Marshal.SizeOf<PdhFmtCounterValueItem>();
            for (var index = 0; index < itemCount; index++)
            {
                var item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(buffer + checked((int)index * itemSize));
                if (item.Value.CStatus is not (PdhCstatusValidData or PdhCstatusNewData))
                {
                    continue;
                }

                var name = Marshal.PtrToStringUni(item.Name);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    samples.Add(new GpuCounterSample(name, item.Value.DoubleValue));
                }
            }

            var aggregate = GpuMetricAggregator.Aggregate(samples, processIds);
            return aggregate is null
                ? TelemetryMetric.Unavailable("当前 GPU 驱动未暴露目标进程计数器。")
                : TelemetryMetric.Available(aggregate.Value);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = PdhCloseQuery(_query);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValue
    {
        public uint CStatus;
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItem
    {
        public nint Name;
        public PdhFmtCounterValue Value;
    }

    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, nuint userData, out nint query);

    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(
        nint query,
        string fullCounterPath,
        nuint userData,
        out nint counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(nint query);

    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static extern uint PdhGetFormattedCounterArrayW(
        nint counter,
        uint format,
        ref uint bufferSize,
        ref uint itemCount,
        nint itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(nint query);
}
