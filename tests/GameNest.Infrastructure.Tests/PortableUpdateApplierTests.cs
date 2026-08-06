using GameNest.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class PortableUpdateApplierTests
{
    private static readonly PortableUpdateTimingOptions FastTiming = new(
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromSeconds(2));

    [Fact]
    public async Task ApplyExchangesDirectoriesAndRemovesRollbackAfterHealthConfirmation()
    {
        using var directory = TemporaryDirectory.Create();
        var fixture = await CreateFixtureAsync(directory.Path, int.MaxValue);
        await File.WriteAllTextAsync(fixture.Plan.HealthFile, "healthy", TestContext.Current.CancellationToken);
        var applier = new PortableUpdateApplier(FastTiming, NullLogger<PortableUpdateApplier>.Instance);

        var result = await applier.ApplyAsync(fixture.PlanFile, TestContext.Current.CancellationToken);

        Assert.Equal(0, result);
        Assert.True(File.Exists(Path.Combine(fixture.Plan.TargetRoot, "new-version.txt")));
        Assert.False(File.Exists(Path.Combine(fixture.Plan.TargetRoot, "old-version.txt")));
        Assert.False(Directory.Exists(fixture.Plan.RollbackRoot));
        Assert.False(File.Exists(fixture.PlanFile));
        await WaitForTestExecutableExitAsync();
    }

    [Fact]
    public async Task ApplyRollsBackProgramAndDatabaseWhenHealthCheckFails()
    {
        using var directory = TemporaryDirectory.Create();
        var fixture = await CreateFixtureAsync(directory.Path, int.MaxValue);
        await File.WriteAllTextAsync(fixture.Plan.DatabaseFile, "migrated", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(fixture.Plan.DatabaseFile + "-wal", "new wal", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(fixture.Plan.DatabaseFile + "-shm", "new shm", TestContext.Current.CancellationToken);
        var applier = new PortableUpdateApplier(FastTiming, NullLogger<PortableUpdateApplier>.Instance);

        var result = await applier.ApplyAsync(fixture.PlanFile, TestContext.Current.CancellationToken);

        Assert.Equal(14, result);
        Assert.True(File.Exists(Path.Combine(fixture.Plan.TargetRoot, "old-version.txt")));
        Assert.Equal(
            "original",
            await File.ReadAllTextAsync(fixture.Plan.DatabaseFile, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(fixture.Plan.DatabaseFile + "-wal"));
        Assert.False(File.Exists(fixture.Plan.DatabaseFile + "-shm"));
    }

    [Fact]
    public async Task ApplyDoesNotForceOldProcessWhenItHasNotExited()
    {
        using var directory = TemporaryDirectory.Create();
        var fixture = await CreateFixtureAsync(directory.Path, Environment.ProcessId);
        var applier = new PortableUpdateApplier(FastTiming, NullLogger<PortableUpdateApplier>.Instance);

        var result = await applier.ApplyAsync(fixture.PlanFile, TestContext.Current.CancellationToken);

        Assert.Equal(11, result);
        Assert.True(File.Exists(Path.Combine(fixture.Plan.TargetRoot, "old-version.txt")));
        Assert.True(Directory.Exists(fixture.Plan.CandidateRoot));
    }

    [Fact]
    public async Task ValidatorRejectsExistingRollbackDirectory()
    {
        using var directory = TemporaryDirectory.Create();
        var fixture = await CreateFixtureAsync(directory.Path, int.MaxValue);
        Directory.CreateDirectory(fixture.Plan.RollbackRoot);

        Assert.Throws<InvalidDataException>(() => PortableUpdatePlanValidator.Validate(fixture.Plan));
    }

    [Fact]
    public async Task DirectoryExchangeRestoresTargetWhenCandidateMoveFails()
    {
        using var directory = TemporaryDirectory.Create();
        var target = Path.Combine(directory.Path, "GameNest");
        var missingCandidate = Path.Combine(directory.Path, "missing-candidate");
        var rollback = Path.Combine(directory.Path, ".rollback");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(
            Path.Combine(target, "old-version.txt"),
            "old",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            PortableDirectoryTransaction.ExchangeAsync(
                target,
                missingCandidate,
                rollback,
                TestContext.Current.CancellationToken));

        Assert.True(File.Exists(Path.Combine(target, "old-version.txt")));
        Assert.False(Directory.Exists(rollback));
    }

    [Fact]
    public async Task WriteProbeReturnsFalseForPathThatCannotContainFiles()
    {
        using var directory = TemporaryDirectory.Create();
        var fileInsteadOfDirectory = Path.Combine(directory.Path, "not-a-directory");
        await File.WriteAllTextAsync(fileInsteadOfDirectory, "file", TestContext.Current.CancellationToken);

        var writable = await PortableInstallWriteProbe.CanWriteAsync(
            fileInsteadOfDirectory,
            TestContext.Current.CancellationToken);

        Assert.False(writable);
    }

    private static async Task<UpdateFixture> CreateFixtureAsync(string root, int currentProcessId)
    {
        var target = Path.Combine(root, "GameNest");
        var candidate = Path.Combine(root, ".GameNest.update-test");
        var rollback = Path.Combine(root, ".GameNest.rollback-test");
        var staging = Path.Combine(root, "staging");
        var stagingPackage = Path.Combine(staging, "package");
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(candidate);
        Directory.CreateDirectory(stagingPackage);
        Directory.CreateDirectory(data);
        await WritePortableMarkerAsync(target);
        await WritePortableMarkerAsync(candidate);
        await File.WriteAllTextAsync(Path.Combine(target, "old-version.txt"), "old", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(candidate, "new-version.txt"), "new", TestContext.Current.CancellationToken);

        var harmlessExecutable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "where.exe");
        File.Copy(harmlessExecutable, Path.Combine(candidate, "GameNest.App.exe"));
        File.Copy(harmlessExecutable, Path.Combine(stagingPackage, "GameNest.App.exe"));
        var database = Path.Combine(data, "gamenest.db");
        var backup = Path.Combine(data, "gamenest.backup.db");
        await File.WriteAllTextAsync(database, "current", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(backup, "original", TestContext.Current.CancellationToken);

        var plan = new PortableUpdatePlan(
            1,
            currentProcessId,
            target,
            candidate,
            rollback,
            staging,
            Path.Combine(staging, "health.ok"),
            Path.Combine(staging, "health.failed"),
            database,
            backup,
            "0.2.1");
        var planFile = Path.Combine(staging, "plan.json");
        await PortableUpdatePlanStore.WriteAsync(planFile, plan, TestContext.Current.CancellationToken);
        return new UpdateFixture(plan, planFile);
    }

    private static Task WritePortableMarkerAsync(string directory) => File.WriteAllTextAsync(
        Path.Combine(directory, ".gamenest-portable-root"),
        "GameNest portable root",
        TestContext.Current.CancellationToken);

    private static async Task WaitForTestExecutableExitAsync()
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("GameNest.App");
            try
            {
                if (processes.Length == 0 || processes.All(static process => process.HasExited))
                {
                    return;
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    private sealed record UpdateFixture(PortableUpdatePlan Plan, string PlanFile);
}
