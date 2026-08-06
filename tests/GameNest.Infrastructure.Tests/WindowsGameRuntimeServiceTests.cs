using System.Collections.Concurrent;
using GameNest.Application;
using GameNest.Domain;
using GameNest.Infrastructure.Windows;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class WindowsGameRuntimeServiceTests
{
    private static readonly DateTimeOffset ProcessStart =
        new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DirectExecutableExitsAndCompletesNaturalSession()
    {
        var snapshots = new ScriptedSnapshotProvider(
            ProcessSnapshot.Empty,
            Snapshot(Process(100, null, @"D:\Games\Direct.exe")),
            ProcessSnapshot.Empty);
        var sessions = new RecordingRuntimeRepository();
        await using var service = CreateService(snapshots, new FakeProcessController(100), sessions);
        var game = CreateGame(@"D:\Games\Direct.exe");
        var stopped = WaitForStatusAsync(
            service,
            runtime => runtime.GameId == game.Id && runtime.State == GameRuntimeState.NotRunning);

        var result = await service.LaunchAsync(game, TestContext.Current.CancellationToken);
        await stopped.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(100, result.ProcessId);
        Assert.Equal(GameProcessConfidence.Confirmed, result.Confidence);
        Assert.Equal(GameExitKind.Natural, sessions.CompletedSession?.ExitKind);
    }

    [Fact]
    public async Task ParentProcessChildIsAdoptedAndRemainsPrimaryAfterParentExit()
    {
        var child = Process(101, 100, @"D:\Games\Child.exe");
        var snapshots = new ScriptedSnapshotProvider(
            ProcessSnapshot.Empty,
            Snapshot(Process(100, null, @"D:\Games\Launcher.exe"), child),
            Snapshot(child),
            Snapshot(child));
        await using var service = CreateService(
            snapshots,
            new FakeProcessController(100),
            new RecordingRuntimeRepository());
        var game = CreateGame(@"D:\Games\Launcher.exe");
        var adopted = WaitForStatusAsync(
            service,
            runtime =>
                runtime.GameId == game.Id &&
                runtime.PrimaryProcessId == 101 &&
                runtime.Confidence == GameProcessConfidence.Confirmed);

        await service.LaunchAsync(game, TestContext.Current.CancellationToken);
        var runtime = await adopted.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(GameRuntimeState.Running, runtime.State);
        Assert.Contains(runtime.Processes, process => process.ProcessId == 101);
    }

    [Fact]
    public async Task ExternalDescendantIsNotConfirmedAsAGameProcess()
    {
        var direct = Process(150, null, @"D:\Games\Launcher.exe");
        var helper = Process(151, 150, @"C:\Program Files\Launcher\Helper.exe");
        var snapshots = new ScriptedSnapshotProvider(
            ProcessSnapshot.Empty,
            Snapshot(direct, helper),
            Snapshot(direct, helper));
        await using var service = CreateService(
            snapshots,
            new FakeProcessController(150),
            new RecordingRuntimeRepository());
        var game = CreateGame(@"D:\Games\Launcher.exe");

        await service.LaunchAsync(game, TestContext.Current.CancellationToken);
        await Task.Delay(80, TestContext.Current.CancellationToken);

        var runtime = service.GetRuntime(game.Id);
        Assert.NotNull(runtime);
        Assert.DoesNotContain(runtime.Processes, process => process.ProcessId == helper.ProcessId);
    }

    [Fact]
    public async Task LauncherMayExitBeforeFirstPostLaunchSnapshotWithoutEndingGame()
    {
        var child = Process(201, 200, @"D:\Games\RealGame.exe");
        var snapshots = new ScriptedSnapshotProvider(
            ProcessSnapshot.Empty,
            Snapshot(child),
            Snapshot(child));
        await using var service = CreateService(
            snapshots,
            new FakeProcessController(200),
            new RecordingRuntimeRepository());
        var game = CreateGame(@"D:\Games\Launcher.exe");
        var adopted = WaitForStatusAsync(
            service,
            runtime => runtime.GameId == game.Id && runtime.PrimaryProcessId == 201);

        await service.LaunchAsync(game, TestContext.Current.CancellationToken);
        var runtime = await adopted.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.True(runtime.IsRunning);
        Assert.Equal(GameProcessConfidence.Confirmed, runtime.Confidence);
    }

    [Fact]
    public async Task ProbableProcessCanBeTrackedButCannotBeStopped()
    {
        var probable = Process(301, null, @"D:\Games\UnknownChild.exe");
        var snapshots = new ScriptedSnapshotProvider(
            ProcessSnapshot.Empty,
            Snapshot(probable),
            Snapshot(probable));
        await using var service = CreateService(
            snapshots,
            new FakeProcessController(300),
            new RecordingRuntimeRepository());
        var game = CreateGame(@"D:\Games\Launcher.exe");
        var tracked = WaitForStatusAsync(
            service,
            runtime => runtime.GameId == game.Id && runtime.PrimaryProcessId == 301);

        await service.LaunchAsync(game, TestContext.Current.CancellationToken);
        var runtime = await tracked.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var stop = await service.StopAsync(game.Id, false, TestContext.Current.CancellationToken);

        Assert.Equal(GameProcessConfidence.Probable, runtime.Confidence);
        Assert.False(runtime.CanStop);
        Assert.Equal(GameStopOutcome.UnsafeTarget, stop.Outcome);
    }

    [Fact]
    public async Task ForceStopRequiresPriorConfirmationResultAndPersistsForcedExit()
    {
        var controller = new FakeProcessController(400);
        var sessions = new RecordingRuntimeRepository();
        await using var service = CreateService(
            new ScriptedSnapshotProvider(ProcessSnapshot.Empty, Snapshot(Process(400, null, @"D:\Games\Stop.exe"))),
            controller,
            sessions,
            monitorInterval: TimeSpan.FromSeconds(1));
        var game = CreateGame(@"D:\Games\Stop.exe");
        await service.LaunchAsync(game, TestContext.Current.CancellationToken);

        var graceful = await service.StopAsync(game.Id, false, TestContext.Current.CancellationToken);
        Assert.Equal(GameStopOutcome.ConfirmationRequired, graceful.Outcome);
        Assert.Empty(controller.KilledProcessIds);

        var forced = await service.StopAsync(game.Id, true, TestContext.Current.CancellationToken);

        Assert.Equal(GameStopOutcome.Stopped, forced.Outcome);
        Assert.Equal([400], controller.KilledProcessIds);
        Assert.Equal(GameExitKind.Forced, sessions.CompletedSession?.ExitKind);
    }

    [Fact]
    public async Task GracefulCloseWaitsForExitAndDoesNotKill()
    {
        var controller = new FakeProcessController(500) { CloseSucceeds = true };
        var sessions = new RecordingRuntimeRepository();
        await using var service = CreateService(
            new ScriptedSnapshotProvider(ProcessSnapshot.Empty, Snapshot(Process(500, null, @"D:\Games\Close.exe"))),
            controller,
            sessions,
            monitorInterval: TimeSpan.FromSeconds(1));
        var game = CreateGame(@"D:\Games\Close.exe", gracefulStopTimeoutSeconds: 1);
        await service.LaunchAsync(game, TestContext.Current.CancellationToken);

        var result = await service.StopAsync(game.Id, false, TestContext.Current.CancellationToken);

        Assert.Equal(GameStopOutcome.Stopped, result.Outcome);
        Assert.Empty(controller.KilledProcessIds);
        Assert.Equal(GameExitKind.Graceful, sessions.CompletedSession?.ExitKind);
    }

    private static WindowsGameRuntimeService CreateService(
        IProcessSnapshotProvider snapshots,
        IGameProcessController controller,
        IGameRuntimeRepository sessions,
        TimeSpan? monitorInterval = null) =>
        new(
            snapshots,
            controller,
            sessions,
            new GameRuntimeOptions(
                monitorInterval ?? TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(35),
                TimeSpan.FromMilliseconds(15),
                TimeSpan.FromMilliseconds(100)),
            NullLogger<WindowsGameRuntimeService>.Instance);

    private static Game CreateGame(string executablePath, int gracefulStopTimeoutSeconds = 10)
    {
        var gameId = Guid.NewGuid();
        var profile = new LaunchProfile(
            Guid.NewGuid(),
            gameId,
            "默认",
            LaunchKind.Executable,
            executablePath,
            null,
            @"D:\Games",
            false,
            true,
            gracefulStopTimeoutSeconds: gracefulStopTimeoutSeconds);
        return new Game(
            gameId,
            "运行测试",
            null,
            @"D:\Games",
            GameSourceType.ManualExecutable,
            false,
            GameAvailability.Available,
            DateTimeOffset.UtcNow,
            null,
            0,
            profile,
            null);
    }

    private static ProcessSnapshotEntry Process(int id, int? parentId, string path) =>
        new(id, parentId, Path.GetFileNameWithoutExtension(path), path, ProcessStart.AddMilliseconds(id));

    private static ProcessSnapshot Snapshot(params ProcessSnapshotEntry[] processes) =>
        new(processes.ToDictionary(static process => process.ProcessId));

    private static Task<GameRuntimeSnapshot> WaitForStatusAsync(
        WindowsGameRuntimeService service,
        Func<GameRuntimeSnapshot, bool> predicate)
    {
        var completion = new TaskCompletionSource<GameRuntimeSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<GameProcessStatusChangedEventArgs>? handler = null;
        handler = (_, args) =>
        {
            if (predicate(args.Runtime))
            {
                service.StatusChanged -= handler;
                completion.TrySetResult(args.Runtime);
            }
        };
        service.StatusChanged += handler;
        return completion.Task;
    }

    private sealed class ScriptedSnapshotProvider(params ProcessSnapshot[] snapshots) : IProcessSnapshotProvider
    {
        private readonly ConcurrentQueue<ProcessSnapshot> _snapshots = new(snapshots);
        private ProcessSnapshot _last = ProcessSnapshot.Empty;

        public Task<ProcessSnapshot> CaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_snapshots.TryDequeue(out var snapshot))
            {
                _last = snapshot;
            }

            return Task.FromResult(_last);
        }
    }

    private sealed class FakeProcessController : IGameProcessController
    {
        private readonly ConcurrentDictionary<int, bool> _alive = new();
        private readonly int _startedProcessId;

        public FakeProcessController(int startedProcessId)
        {
            _startedProcessId = startedProcessId;
            _alive[startedProcessId] = true;
        }

        public bool CloseSucceeds { get; init; }

        public List<int> KilledProcessIds { get; } = [];

        public Task<StartedProcess> StartAsync(Game game, CancellationToken cancellationToken) =>
            Task.FromResult(
                new StartedProcess(
                    _startedProcessId,
                    ProcessStart.AddMilliseconds(_startedProcessId)));

        public Task<bool> IsAliveAsync(
            int processId,
            DateTimeOffset? expectedStartTimeUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult(_alive.GetValueOrDefault(processId));

        public Task<bool> TryCloseMainWindowAsync(
            int processId,
            DateTimeOffset? expectedStartTimeUtc,
            CancellationToken cancellationToken)
        {
            if (CloseSucceeds)
            {
                _alive[processId] = false;
            }

            return Task.FromResult(CloseSucceeds);
        }

        public Task KillAsync(
            int processId,
            DateTimeOffset? expectedStartTimeUtc,
            CancellationToken cancellationToken)
        {
            KilledProcessIds.Add(processId);
            _alive[processId] = false;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRuntimeRepository : IGameRuntimeRepository
    {
        public PlaySession? ActiveSession { get; private set; }

        public PlaySession? CompletedSession { get; private set; }

        public Task StartSessionAsync(PlaySession session, CancellationToken cancellationToken)
        {
            ActiveSession = session;
            return Task.CompletedTask;
        }

        public Task UpdateTrackedProcessIdsAsync(
            Guid sessionId,
            IReadOnlyCollection<int> processIds,
            CancellationToken cancellationToken)
        {
            ActiveSession = ActiveSession?.WithTrackedProcessIds(processIds);
            return Task.CompletedTask;
        }

        public Task<PlaySession?> CompleteSessionAsync(
            Guid sessionId,
            DateTimeOffset endedAtUtc,
            GameExitKind exitKind,
            CancellationToken cancellationToken)
        {
            if (ActiveSession is null)
            {
                return Task.FromResult<PlaySession?>(CompletedSession);
            }

            CompletedSession = ActiveSession.Complete(endedAtUtc, exitKind);
            ActiveSession = null;
            return Task.FromResult<PlaySession?>(CompletedSession);
        }

        public Task<IReadOnlyList<PlaySession>> GetSessionsAsync(
            Guid gameId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlaySession>>(
                CompletedSession is null ? [] : [CompletedSession]);

        public Task<IReadOnlyList<PlaySession>> GetActiveSessionsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlaySession>>(
                ActiveSession is null ? [] : [ActiveSession]);
    }
}
