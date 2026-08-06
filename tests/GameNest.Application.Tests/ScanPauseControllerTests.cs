using GameNest.Application;

namespace GameNest.Application.Tests;

public sealed class ScanPauseControllerTests
{
    [Fact]
    public async Task PausedWorkWaitsUntilResume()
    {
        var controller = new ScanPauseController();
        controller.Pause();

        var wait = controller.WaitWhilePausedAsync(TestContext.Current.CancellationToken);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.False(wait.IsCompleted);

        controller.Resume();
        await wait;
        Assert.False(controller.IsPaused);
    }

    [Fact]
    public async Task PausedWorkHonorsCancellation()
    {
        var controller = new ScanPauseController();
        controller.Pause();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.WaitWhilePausedAsync(cancellation.Token));
    }
}
