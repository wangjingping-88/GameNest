using System.ComponentModel;
using System.Runtime.InteropServices;
using GameNest.Application;

namespace GameNest.Infrastructure.Windows;

public sealed class WindowsVolumeIdentityService : IVolumeIdentityService
{
    public Task<VolumeLocation> ResolveAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() => ResolveCore(path, requirePath: true, cancellationToken), cancellationToken);
    }

    public Task<VolumeLocation?> FindAsync(
        string volumeIdentity,
        string relativePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeIdentity);
        return Task.Run(
            () =>
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (!drive.IsReady)
                        {
                            continue;
                        }

                        var location = ResolveCore(drive.RootDirectory.FullName, requirePath: false, cancellationToken);
                        if (!location.Identity.Equals(volumeIdentity, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var currentPath = string.IsNullOrWhiteSpace(relativePath)
                            ? location.VolumeRoot
                            : Path.GetFullPath(Path.Combine(location.VolumeRoot, relativePath));
                        return new VolumeLocation(
                            location.Identity,
                            location.VolumeRoot,
                            currentPath,
                            relativePath,
                            Directory.Exists(currentPath));
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }

                return null;
            },
            cancellationToken);
    }

    private static VolumeLocation ResolveCore(
        string path,
        bool requirePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path.Trim());
        if (requirePath && !Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException("所选扫描目录不存在或当前磁盘未连接。");
        }

        var volumePath = new char[32768];
        if (!GetVolumePathName(fullPath, volumePath, volumePath.Length))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法识别所选目录所在的磁盘。");
        }

        var volumeRoot = Path.GetFullPath(ReadNullTerminated(volumePath));
        var volumeName = new char[32768];
        if (!GetVolumeNameForVolumeMountPoint(volumeRoot, volumeName, volumeName.Length))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取磁盘的稳定卷标识。");
        }

        var relativePath = Path.GetRelativePath(volumeRoot, fullPath);
        if (relativePath == ".")
        {
            relativePath = string.Empty;
        }

        return new VolumeLocation(
            ReadNullTerminated(volumeName),
            volumeRoot,
            fullPath,
            relativePath,
            IsOnline: true);
    }

    private static string ReadNullTerminated(char[] buffer)
    {
        var length = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, length < 0 ? buffer.Length : length);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(
        string fileName,
        [Out] char[] volumePathName,
        int bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string volumeMountPoint,
        [Out] char[] volumeName,
        int bufferLength);
}
