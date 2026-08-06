using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using GameNest.Application;
using GameNest.Domain;

namespace GameNest.Infrastructure.Windows;

public sealed class WindowsLocalGameFileInspector : ILocalGameFileInspector
{
    public Task<LocalGameFileInspection> InspectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() => InspectCore(path, cancellationToken), cancellationToken);
    }

    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return File.Exists(path);
            },
            cancellationToken);
    }

    private static LocalGameFileInspection InspectCore(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = Path.GetFullPath(path.Trim());
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("所选文件不存在或当前不可访问。", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return InspectExecutable(sourcePath);
        }

        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return InspectShortcut(sourcePath);
        }

        throw new NotSupportedException("只能添加 .exe 或 .lnk 快捷方式。");
    }

    private static LocalGameFileInspection InspectExecutable(string executablePath)
    {
        var workingDirectory = Path.GetDirectoryName(executablePath)
            ?? throw new InvalidOperationException("无法确定游戏工作目录。");
        return new LocalGameFileInspection(
            executablePath,
            executablePath,
            GetExecutableTitle(executablePath),
            null,
            workingDirectory,
            GameSourceType.ManualExecutable,
            LaunchKind.Executable,
            executablePath);
    }

    private static LocalGameFileInspection InspectShortcut(string shortcutPath)
    {
        var shellLinkType = Type.GetTypeFromCLSID(
            new Guid("00021401-0000-0000-C000-000000000046"),
            throwOnError: true)
            ?? throw new InvalidOperationException("Windows 快捷方式组件不可用。");
        var shellLink = (IShellLinkW)(Activator.CreateInstance(shellLinkType)
            ?? throw new InvalidOperationException("无法创建 Windows 快捷方式解析器。"));
        try
        {
            ((IPersistFile)shellLink).Load(shortcutPath, 0);
            var targetPath = new StringBuilder(32768);
            shellLink.GetPath(targetPath, targetPath.Capacity, out _, 0);
            var executablePath = Environment.ExpandEnvironmentVariables(targetPath.ToString().Trim());
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                throw new FileNotFoundException("快捷方式指向的主程序不存在或当前不可访问。", executablePath);
            }

            if (!Path.GetExtension(executablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("当前阶段只支持指向 EXE 的快捷方式。");
            }

            var argumentsBuffer = new StringBuilder(32768);
            shellLink.GetArguments(argumentsBuffer, argumentsBuffer.Capacity);
            var workingDirectoryBuffer = new StringBuilder(32768);
            shellLink.GetWorkingDirectory(workingDirectoryBuffer, workingDirectoryBuffer.Capacity);
            var workingDirectory = Environment.ExpandEnvironmentVariables(workingDirectoryBuffer.ToString().Trim());
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                workingDirectory = Path.GetDirectoryName(executablePath)
                    ?? throw new InvalidOperationException("无法确定快捷方式的工作目录。");
            }

            var iconPathBuffer = new StringBuilder(32768);
            shellLink.GetIconLocation(iconPathBuffer, iconPathBuffer.Capacity, out _);
            var iconPath = Environment.ExpandEnvironmentVariables(iconPathBuffer.ToString().Trim());
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
            {
                iconPath = executablePath;
            }

            return new LocalGameFileInspection(
                shortcutPath,
                Path.GetFullPath(executablePath),
                Path.GetFileNameWithoutExtension(shortcutPath),
                NullIfWhiteSpace(argumentsBuffer.ToString()),
                Path.GetFullPath(workingDirectory),
                GameSourceType.ManualShortcut,
                LaunchKind.Shortcut,
                Path.GetFullPath(iconPath));
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static string GetExecutableTitle(string executablePath)
    {
        var version = FileVersionInfo.GetVersionInfo(executablePath);
        return FirstNonEmpty(
            version.ProductName,
            version.FileDescription,
            Path.GetFileNameWithoutExtension(executablePath));
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(static value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out] StringBuilder pszFile, int cch, out Win32FindDataW pfd, uint flags);
        void GetIdList(out nint ppidl);
        void SetIdList(nint pidl);
        void GetDescription([Out] StringBuilder pszName, int cch);
        void SetDescription(string pszName);
        void GetWorkingDirectory([Out] StringBuilder pszDir, int cch);
        void SetWorkingDirectory(string pszDir);
        void GetArguments([Out] StringBuilder pszArgs, int cch);
        void SetArguments(string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCommand(out int piShowCmd);
        void SetShowCommand(int iShowCmd);
        void GetIconLocation([Out] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation(string pszIconPath, int iIcon);
        void SetRelativePath(string pszPathRel, uint reserved);
        void Resolve(nint hwnd, uint flags);
        void SetPath(string pszFile);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindDataW
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint Reserved0;
        public uint Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string AlternateFileName;
    }
}
