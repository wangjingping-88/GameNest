namespace GameNest.Application;

public sealed class ScanPauseController : IScanPauseToken
{
    private readonly object _gate = new();
    private TaskCompletionSource? _resumeSource;

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _resumeSource is not null;
            }
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            _resumeSource ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource? resumeSource;
        lock (_gate)
        {
            resumeSource = _resumeSource;
            _resumeSource = null;
        }

        resumeSource?.TrySetResult();
    }

    public async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task? resumeTask;
            lock (_gate)
            {
                resumeTask = _resumeSource?.Task;
            }

            if (resumeTask is null)
            {
                return;
            }

            await resumeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
