using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using GameNest.Application;
using GameNest.Domain;

namespace GameNest.Infrastructure.Windows;

public sealed class WindowsProcessSnapshotProvider : IProcessSnapshotProvider
{
    public Task<ProcessSnapshot> CaptureAsync(CancellationToken cancellationToken) =>
        Task.Run(() => CaptureCore(cancellationToken), cancellationToken);

    private static ProcessSnapshot CaptureCore(CancellationToken cancellationToken)
    {
        var parents = CaptureParentProcessIds();
        var entries = new Dictionary<int, ProcessSnapshotEntry>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var processId = process.Id;
                    var processName = SafeRead(() => process.ProcessName)
                        ?? processId.ToString(CultureInfo.InvariantCulture);
                    var executablePath = SafeRead(() => process.MainModule?.FileName);
                    var startTime = SafeRead(
                        () => new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero));
                    entries[processId] = new ProcessSnapshotEntry(
                        processId,
                        parents.GetValueOrDefault(processId),
                        processName,
                        executablePath,
                        startTime);
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
                {
                    // 进程可能在快照期间退出，或当前权限无法读取；跳过单个进程即可。
                }
            }
        }

        return new ProcessSnapshot(entries);
    }

    private static Dictionary<int, int?> CaptureParentProcessIds()
    {
        var result = new Dictionary<int, int?>();
        var snapshot = CreateToolhelp32Snapshot(0x00000002, 0);
        if (snapshot == new IntPtr(-1))
        {
            return result;
        }

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
            {
                return result;
            }

            do
            {
                result[(int)entry.ProcessId] = entry.ParentProcessId == 0
                    ? null
                    : (int)entry.ParentProcessId;
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));

            return result;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    private static T? SafeRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return default;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }
}

public sealed class WindowsGameProcessController : IGameProcessController
{
    public Task<StartedProcess> StartAsync(Game game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        return Task.Run(() => StartCore(game, cancellationToken), cancellationToken);
    }

    public Task<bool> IsAliveAsync(
        int processId,
        DateTimeOffset? expectedStartTimeUtc,
        CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var process = TryGetMatchingProcess(processId, expectedStartTimeUtc);
                return process is not null && !process.HasExited;
            },
            cancellationToken);

    public Task<bool> TryCloseMainWindowAsync(
        int processId,
        DateTimeOffset? expectedStartTimeUtc,
        CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var process = TryGetMatchingProcess(processId, expectedStartTimeUtc);
                return process is not null && !process.HasExited && process.CloseMainWindow();
            },
            cancellationToken);

    public Task KillAsync(
        int processId,
        DateTimeOffset? expectedStartTimeUtc,
        CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var process = TryGetMatchingProcess(processId, expectedStartTimeUtc)
                    ?? throw new InvalidOperationException("目标进程已经退出或 PID 已被复用，未执行强制结束。");
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: false);
                }
            },
            cancellationToken);

    private static StartedProcess StartCore(Game game, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = game.LaunchProfile;
        if (!File.Exists(profile.ExecutablePath))
        {
            throw new FileNotFoundException("游戏主程序不存在。", profile.ExecutablePath);
        }

        if (!Directory.Exists(profile.WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"游戏工作目录不存在：{profile.WorkingDirectory}");
        }

        var process = Process.Start(
            new ProcessStartInfo
            {
                FileName = profile.ExecutablePath,
                Arguments = profile.Arguments ?? string.Empty,
                WorkingDirectory = profile.WorkingDirectory,
                UseShellExecute = profile.RunAsAdministrator,
                Verb = profile.RunAsAdministrator ? "runas" : string.Empty,
            }) ?? throw new InvalidOperationException("Windows 未返回可跟踪的启动进程。");
        using (process)
        {
            DateTimeOffset? startTime = null;
            try
            {
                startTime = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
            }

            return new StartedProcess(process.Id, startTime);
        }
    }

    private static Process? TryGetMatchingProcess(int processId, DateTimeOffset? expectedStartTimeUtc)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (expectedStartTimeUtc is null)
            {
                return process;
            }

            var actual = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            if (Math.Abs((actual - expectedStartTimeUtc.Value).TotalSeconds) <= 1)
            {
                return process;
            }

            process.Dispose();
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }
}
