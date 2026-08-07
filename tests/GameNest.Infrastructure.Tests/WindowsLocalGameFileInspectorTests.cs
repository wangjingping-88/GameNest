using GameNest.Domain;
using GameNest.Infrastructure.Windows;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Globalization;

namespace GameNest.Infrastructure.Tests;

public sealed class WindowsLocalGameFileInspectorTests
{
    [Fact]
    public async Task InspectAsyncAcceptsExecutableInUnicodeSpaceAndSpecialCharacterPath()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceExecutable = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("测试环境缺少 ComSpec。");
        var targetDirectory = Path.Combine(directory.Path, "中文 游戏 [特别版] #1");
        Directory.CreateDirectory(targetDirectory);
        var targetExecutable = Path.Combine(targetDirectory, "启动 游戏.exe");
        File.Copy(sourceExecutable, targetExecutable);
        var inspector = new WindowsLocalGameFileInspector();

        var inspection = await inspector.InspectAsync(
            targetExecutable,
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.GetFullPath(targetExecutable), inspection.ExecutablePath);
        Assert.Equal(targetDirectory, inspection.WorkingDirectory);
        Assert.Equal(GameSourceType.ManualExecutable, inspection.SourceType);
        Assert.Equal(LaunchKind.Executable, inspection.LaunchKind);
    }

    [Fact]
    public async Task InspectAsyncRejectsUnsupportedFileType()
    {
        using var directory = TemporaryDirectory.Create();
        var textPath = Path.Combine(directory.Path, "不是游戏.txt");
        await File.WriteAllTextAsync(
            textPath,
            "test",
            TestContext.Current.CancellationToken);
        var inspector = new WindowsLocalGameFileInspector();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => inspector.InspectAsync(textPath, TestContext.Current.CancellationToken));

        Assert.Contains(".exe", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".lnk", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(
        Skip = "需要交互式 Windows Shell 会话；GitHub Hosted Runner 的非交互会话不支持此集成测试。",
        SkipUnless = nameof(SupportsInteractiveShellShortcutIntegration))]
    public async Task InspectAsyncResolvesShortcutTargetArgumentsAndWorkingDirectory()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceExecutable = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("测试环境缺少 ComSpec。");
        var targetDirectory = Path.Combine(directory.Path, "快捷方式 中文 [测试]");
        Directory.CreateDirectory(targetDirectory);
        var targetExecutable = Path.Combine(targetDirectory, "目标 游戏.exe");
        File.Copy(sourceExecutable, targetExecutable);
        var shortcutPath = Path.Combine(directory.Path, "我的 游戏.lnk");
        CreateShortcut(shortcutPath, targetExecutable, "--测试 参数", targetDirectory);
        var inspector = new WindowsLocalGameFileInspector();

        var inspection = await inspector.InspectAsync(
            shortcutPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.GetFileName(targetExecutable), Path.GetFileName(inspection.ExecutablePath));
        Assert.Equal("--测试 参数", inspection.Arguments);
        Assert.Equal(Path.GetFileName(targetDirectory), Path.GetFileName(inspection.WorkingDirectory));
        Assert.Equal(GameSourceType.ManualShortcut, inspection.SourceType);
        Assert.Equal(LaunchKind.Shortcut, inspection.LaunchKind);
    }

    public static bool SupportsInteractiveShellShortcutIntegration =>
        OperatingSystem.IsWindows() &&
        Environment.UserInteractive &&
        !string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string arguments,
        string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)
            ?? throw new InvalidOperationException("测试环境缺少 WScript.Shell。");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("无法创建 WScript.Shell。");
        object? shortcut = null;
        try
        {
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                shell,
                [shortcutPath],
                CultureInfo.InvariantCulture);
            var shortcutType = shortcut?.GetType()
                ?? throw new InvalidOperationException("无法创建测试快捷方式。");
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [targetPath], CultureInfo.InvariantCulture);
            shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, [arguments], CultureInfo.InvariantCulture);
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [workingDirectory], CultureInfo.InvariantCulture);
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null, CultureInfo.InvariantCulture);
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
}
