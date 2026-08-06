using GameNest.Application;
using GameNest.Domain;
using GameNest.Infrastructure;
using GameNest.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class SqliteGameScanRepositoryTests
{
    [Fact]
    public async Task RootCandidateAndExclusionPersistAcrossRepositoryInstances()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = GameNestDataPaths.CreateForRoot(directory.Path);
        var rootId = Guid.NewGuid();
        var root = new ScanRoot(
            rootId,
            "test-volume",
            directory.Path,
            "Games",
            ScanMode.Quick,
            true,
            true,
            null,
            null);
        var candidate = CreateCandidate(rootId, directory.Path);

        using (var initializer = CreateInitializer(paths))
        {
            var repository = new SqliteGameScanRepository(paths, initializer);
            await repository.AddRootAsync(root, TestContext.Current.CancellationToken);
            var runId = await repository.StartRunAsync(ScanMode.Quick, TestContext.Current.CancellationToken);
            await repository.SaveCandidatesAsync(runId, [candidate], TestContext.Current.CancellationToken);
            await repository.AddExcludedDirectoryAsync(directory.Path, TestContext.Current.CancellationToken);
            await repository.CompleteRunAsync(
                runId,
                GameScanRunStatus.Completed,
                4,
                1,
                null,
                TestContext.Current.CancellationToken);
        }

        using (var restartedInitializer = CreateInitializer(paths))
        {
            var repository = new SqliteGameScanRepository(paths, restartedInitializer);
            var storedRoot = Assert.Single(await repository.GetRootsAsync(TestContext.Current.CancellationToken));
            var storedCandidate = Assert.Single(await repository.GetCandidatesAsync(TestContext.Current.CancellationToken));
            var exclusion = Assert.Single(await repository.GetExcludedDirectoriesAsync(TestContext.Current.CancellationToken));

            Assert.Equal(root.Id, storedRoot.Id);
            Assert.Equal(candidate.ExecutablePath, storedCandidate.ExecutablePath);
            Assert.Equal(candidate.Evidence, storedCandidate.Evidence);
            Assert.Equal(Path.TrimEndingDirectorySeparator(directory.Path), exclusion);
            Assert.Equal(exclusion, await repository.UndoLastExcludedDirectoryAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await repository.GetExcludedDirectoriesAsync(TestContext.Current.CancellationToken));
        }
    }

    private static SqliteDatabaseInitializer CreateInitializer(GameNestDataPaths paths) =>
        new(paths, NullLogger<SqliteDatabaseInitializer>.Instance);

    private static GameCandidate CreateCandidate(Guid rootId, string installRoot) =>
        new(
            Guid.NewGuid(),
            rootId,
            "test",
            GameCandidateSource.GenericExecutable,
            null,
            "Example",
            Path.Combine(installRoot, "Example.exe"),
            null,
            installRoot,
            installRoot,
            "test-volume",
            "fingerprint",
            80,
            [new GameCandidateEvidence("test", "测试证据", 80)],
            installRoot.ToUpperInvariant(),
            true,
            GameCandidateDecision.Pending,
            DateTimeOffset.UtcNow);
}
