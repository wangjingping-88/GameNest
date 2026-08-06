using GameNest.Application;
using GameNest.Domain;
using GameNest.Infrastructure.Windows;

namespace GameNest.Infrastructure.Tests;

public sealed class WindowsGameProcessControllerTests
{
    [Fact]
    public async Task StartAndKillValidateProcessStartTimeToPreventPidReuse()
    {
        var executablePath = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("测试环境缺少 ComSpec。");
        var game = CreateGame(executablePath, "/c ping -n 8 127.0.0.1 > nul");
        var controller = new WindowsGameProcessController();
        var started = await controller.StartAsync(game, TestContext.Current.CancellationToken);

        try
        {
            Assert.True(
                await controller.IsAliveAsync(
                    started.ProcessId,
                    started.StartTimeUtc,
                    TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => controller.KillAsync(
                    started.ProcessId,
                    started.StartTimeUtc?.AddHours(1),
                    TestContext.Current.CancellationToken));

            await controller.KillAsync(
                started.ProcessId,
                started.StartTimeUtc,
                TestContext.Current.CancellationToken);
            await WaitUntilStoppedAsync(controller, started);
        }
        finally
        {
            if (await controller.IsAliveAsync(
                    started.ProcessId,
                    started.StartTimeUtc,
                    CancellationToken.None))
            {
                await controller.KillAsync(started.ProcessId, started.StartTimeUtc, CancellationToken.None);
            }
        }
    }

    private static async Task WaitUntilStoppedAsync(
        WindowsGameProcessController controller,
        StartedProcess started)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!await controller.IsAliveAsync(
                    started.ProcessId,
                    started.StartTimeUtc,
                    TestContext.Current.CancellationToken))
            {
                return;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail("测试进程未在 5 秒内退出。");
    }

    private static Game CreateGame(string executablePath, string arguments)
    {
        var gameId = Guid.NewGuid();
        var workingDirectory = Path.GetDirectoryName(executablePath)!;
        return new Game(
            gameId,
            "进程控制测试",
            null,
            workingDirectory,
            GameSourceType.ManualExecutable,
            false,
            GameAvailability.Available,
            DateTimeOffset.UtcNow,
            null,
            0,
            new LaunchProfile(
                Guid.NewGuid(),
                gameId,
                "默认",
                LaunchKind.Executable,
                executablePath,
                arguments,
                workingDirectory,
                false,
                true),
            null);
    }
}
