using System.Diagnostics;
using GameNest.Application;
using Microsoft.Extensions.Logging;

namespace GameNest.Infrastructure.Updates;

public sealed record PortableUpdateTimingOptions(
    TimeSpan OldProcessExitTimeout,
    TimeSpan OverlayExitTimeout,
    TimeSpan HealthConfirmationTimeout)
{
    public static PortableUpdateTimingOptions Default { get; } = new(
        TimeSpan.FromSeconds(45),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(60));
}

public sealed class PortableUpdateApplier(
    PortableUpdateTimingOptions timing,
    ILogger<PortableUpdateApplier> logger) : IPortableUpdateApplier
{
    private static readonly Action<ILogger, Exception?> PlanValidationFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(6200, nameof(PlanValidationFailed)),
        "升级计划验证失败。");
    private static readonly Action<ILogger, Exception?> DirectoryExchangeFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(6201, nameof(DirectoryExchangeFailed)),
        "升级目录交换失败，正在恢复旧版本。");
    private static readonly Action<ILogger, Exception?> UpdatedApplicationFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(6202, nameof(UpdatedApplicationFailed)),
        "新版 GameNest 启动或健康检查失败，正在回滚。");
    private static readonly Action<ILogger, Exception?> HealthConfirmationTimedOut = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(6203, nameof(HealthConfirmationTimedOut)),
        "新版 GameNest 仍在运行但未及时写入健康确认；保留回滚目录供人工处理。");
    public async Task<int> ApplyAsync(string planFile, CancellationToken cancellationToken)
    {
        PortableUpdatePlan plan;
        try
        {
            plan = PortableUpdatePlanValidator.Validate(
                await PortableUpdatePlanStore.ReadAsync(planFile, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PlanValidationFailed(logger, exception);
            return 10;
        }

        if (!await WaitForOldProcessesAsync(plan, cancellationToken).ConfigureAwait(false))
        {
            await WriteFailureAsync(plan, "旧版 GameNest 或 Overlay 未在限定时间内正常退出。", cancellationToken)
                .ConfigureAwait(false);
            return 11;
        }

        try
        {
            await PortableDirectoryTransaction
                .ExchangeAsync(plan.TargetRoot, plan.CandidateRoot, plan.RollbackRoot, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DirectoryExchangeFailed(logger, exception);
            await RestoreDirectoryExchangeAsync(plan, restoreDatabase: false, cancellationToken).ConfigureAwait(false);
            await WriteFailureAsync(plan, "升级目录交换失败，已尝试保留旧版本。", cancellationToken)
                .ConfigureAwait(false);
            return 12;
        }

        Process? newProcess = null;
        try
        {
            newProcess = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(plan.TargetRoot, "GameNest.App.exe"),
                Arguments = $"--complete-update \"{planFile}\"",
                WorkingDirectory = plan.TargetRoot,
                UseShellExecute = false,
            });
            if (newProcess is null)
            {
                throw new InvalidOperationException("无法启动更新后的 GameNest。");
            }

            var health = await WaitForHealthAsync(plan, newProcess, cancellationToken).ConfigureAwait(false);
            if (health == HealthOutcome.Healthy)
            {
                await CleanupAfterSuccessAsync(plan, planFile, cancellationToken).ConfigureAwait(false);
                return 0;
            }

            if (health == HealthOutcome.TimedOutWhileRunning)
            {
                HealthConfirmationTimedOut(logger, null);
                return 13;
            }

            if (!newProcess.HasExited &&
                !await WaitForProcessExitAsync(newProcess, timing.OldProcessExitTimeout, cancellationToken)
                    .ConfigureAwait(false))
            {
                HealthConfirmationTimedOut(logger, null);
                return 13;
            }

            await RestoreDirectoryExchangeAsync(plan, restoreDatabase: true, cancellationToken).ConfigureAwait(false);
            RelaunchOldVersion(plan.TargetRoot);
            return 14;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            UpdatedApplicationFailed(logger, exception);
            if (newProcess is null || newProcess.HasExited)
            {
                await RestoreDirectoryExchangeAsync(plan, restoreDatabase: true, cancellationToken).ConfigureAwait(false);
                RelaunchOldVersion(plan.TargetRoot);
            }

            return 15;
        }
        finally
        {
            newProcess?.Dispose();
        }
    }

    private async Task<bool> WaitForOldProcessesAsync(
        PortableUpdatePlan plan,
        CancellationToken cancellationToken)
    {
        try
        {
            using var oldProcess = Process.GetProcessById(plan.CurrentProcessId);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timing.OldProcessExitTimeout);
            await oldProcess.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var overlayDeadline = DateTimeOffset.UtcNow + timing.OverlayExitTimeout;
        while (DateTimeOffset.UtcNow < overlayDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var running = Process.GetProcessesByName("GameNest.Overlay");
            try
            {
                if (running.Length == 0)
                {
                    return true;
                }
            }
            finally
            {
                foreach (var process in running)
                {
                    process.Dispose();
                }
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<HealthOutcome> WaitForHealthAsync(
        PortableUpdatePlan plan,
        Process process,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timing.HealthConfirmationTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(plan.HealthFile))
            {
                return HealthOutcome.Healthy;
            }

            if (File.Exists(plan.FailureFile) || process.HasExited)
            {
                return HealthOutcome.Failed;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return process.HasExited ? HealthOutcome.Failed : HealthOutcome.TimedOutWhileRunning;
    }

    private static async Task RestoreDirectoryExchangeAsync(
        PortableUpdatePlan plan,
        bool restoreDatabase,
        CancellationToken cancellationToken)
    {
        await PortableDirectoryTransaction
            .RestoreAsync(plan.TargetRoot, plan.RollbackRoot, cancellationToken)
            .ConfigureAwait(false);
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (restoreDatabase && File.Exists(plan.DatabaseBackupFile))
            {
                var databaseDirectory = Path.GetDirectoryName(plan.DatabaseFile)
                                        ?? throw new InvalidDataException("无法确定数据库目录。");
                Directory.CreateDirectory(databaseDirectory);
                foreach (var sidecar in new[] { plan.DatabaseFile + "-wal", plan.DatabaseFile + "-shm" })
                {
                    if (File.Exists(sidecar))
                    {
                        File.Delete(sidecar);
                    }
                }
                File.Copy(plan.DatabaseBackupFile, plan.DatabaseFile, overwrite: true);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> WaitForProcessExitAsync(
        Process process,
        TimeSpan timeoutValue,
        CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            return true;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutValue);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task CleanupAfterSuccessAsync(
        PortableUpdatePlan plan,
        string planFile,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(plan.RollbackRoot))
            {
                Directory.Delete(plan.RollbackRoot, recursive: true);
            }

            if (File.Exists(planFile))
            {
                File.Delete(planFile);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteFailureAsync(
        PortableUpdatePlan plan,
        string message,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(plan.FailureFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            await Task.Run(() => Directory.CreateDirectory(directory), cancellationToken).ConfigureAwait(false);
        }

        await File.WriteAllTextAsync(plan.FailureFile, message, cancellationToken).ConfigureAwait(false);
    }

    private static void RelaunchOldVersion(string targetRoot)
    {
        var executable = Path.Combine(targetRoot, "GameNest.App.exe");
        if (!File.Exists(executable))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = targetRoot,
            UseShellExecute = false,
        });
    }

    private enum HealthOutcome
    {
        Healthy,
        Failed,
        TimedOutWhileRunning,
    }
}
