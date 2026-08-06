using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
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
        var shellType = Type.GetTypeFromProgID(
            "WScript.Shell",
            throwOnError: true)
            ?? throw new InvalidOperationException("Windows 快捷方式组件不可用。");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("无法创建 Windows 快捷方式解析器。");
        object? shortcut = null;
        try
        {
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath],
                culture: CultureInfo.InvariantCulture);
            var shortcutType = shortcut?.GetType()
                ?? throw new InvalidOperationException("无法读取 Windows 快捷方式。");
            var executablePath = Environment.ExpandEnvironmentVariables(
                GetShortcutProperty(shortcutType, shortcut, "TargetPath"));
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                throw new FileNotFoundException("快捷方式指向的主程序不存在或当前不可访问。", executablePath);
            }

            if (!Path.GetExtension(executablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("当前阶段只支持指向 EXE 的快捷方式。");
            }

            var arguments = GetShortcutProperty(shortcutType, shortcut, "Arguments");
            var workingDirectory = Environment.ExpandEnvironmentVariables(
                GetShortcutProperty(shortcutType, shortcut, "WorkingDirectory"));
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                workingDirectory = Path.GetDirectoryName(executablePath)
                    ?? throw new InvalidOperationException("无法确定快捷方式的工作目录。");
            }

            var iconPath = Environment.ExpandEnvironmentVariables(
                GetIconPath(GetShortcutProperty(shortcutType, shortcut, "IconLocation")));
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
            {
                iconPath = executablePath;
            }

            return new LocalGameFileInspection(
                shortcutPath,
                Path.GetFullPath(executablePath),
                Path.GetFileNameWithoutExtension(shortcutPath),
                NullIfWhiteSpace(arguments),
                Path.GetFullPath(workingDirectory),
                GameSourceType.ManualShortcut,
                LaunchKind.Shortcut,
                Path.GetFullPath(iconPath));
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static string GetShortcutProperty(Type shortcutType, object shortcut, string propertyName) =>
        (shortcutType.InvokeMember(
            propertyName,
            BindingFlags.GetProperty,
            binder: null,
            target: shortcut,
            args: null,
            culture: CultureInfo.InvariantCulture) as string ?? string.Empty).Trim();

    private static string GetIconPath(string iconLocation)
    {
        var separator = iconLocation.LastIndexOf(',');
        var iconPath = separator >= 0 ? iconLocation[..separator] : iconLocation;
        return iconPath.Trim().Trim('"');
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

}
