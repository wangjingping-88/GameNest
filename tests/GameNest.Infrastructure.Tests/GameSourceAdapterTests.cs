using GameNest.Application;
using GameNest.Domain;
using GameNest.Infrastructure.Scanning;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameNest.Infrastructure.Tests;

public sealed class GameSourceAdapterTests
{
    [Fact]
    public async Task SteamManifestSelectsGameExecutableAndSkipsUninstaller()
    {
        using var directory = TemporaryDirectory.Create();
        var steamApps = Directory.CreateDirectory(Path.Combine(directory.Path, "steamapps"));
        await File.WriteAllTextAsync(
            Path.Combine(steamApps.FullName, "libraryfolders.vdf"),
            "\"libraryfolders\" { }",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(steamApps.FullName, "appmanifest_100.acf"),
            "\"AppState\" { \"appid\" \"100\" \"name\" \"Example Game\" \"installdir\" \"Example\" }",
            TestContext.Current.CancellationToken);
        var installRoot = Directory.CreateDirectory(Path.Combine(steamApps.FullName, "common", "Example"));
        await File.WriteAllBytesAsync(
            Path.Combine(installRoot.FullName, "ExampleGame.exe"),
            new byte[1024 * 1024],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(installRoot.FullName, "uninstall.exe"),
            new byte[2048],
            TestContext.Current.CancellationToken);

        var adapter = new SteamGameSourceAdapter(NullLogger<SteamGameSourceAdapter>.Instance);
        var candidates = await adapter.ScanAsync(
            CreateContext(directory.Path),
            null,
            TestContext.Current.CancellationToken);

        var candidate = Assert.Single(candidates);
        Assert.EndsWith("ExampleGame.exe", candidate.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("100", candidate.SourceGameId);
        Assert.Equal(GameCandidateConfidence.High, new GameCandidateScorer().Score(candidate, DateTimeOffset.UtcNow).Confidence);
    }

    [Fact]
    public async Task GenericScanUsesSignalsAndMissingDiskDoesNotCrash()
    {
        using var directory = TemporaryDirectory.Create();
        var gameDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "Games", "Example"));
        await File.WriteAllBytesAsync(
            Path.Combine(gameDirectory.FullName, "Example.exe"),
            new byte[1024 * 1024],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(gameDirectory.FullName, "steam_api64.dll"),
            [1],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(gameDirectory.FullName, "UnityPlayer.dll"),
            [1],
            TestContext.Current.CancellationToken);
        var adapter = new GenericExecutableGameSourceAdapter(
            NullLogger<GenericExecutableGameSourceAdapter>.Instance);

        var candidates = await adapter.ScanAsync(
            CreateContext(directory.Path),
            null,
            TestContext.Current.CancellationToken);
        var scored = candidates
            .Select(candidate => new GameCandidateScorer().Score(candidate, DateTimeOffset.UtcNow))
            .ToArray();

        Assert.Contains(scored, static candidate =>
            candidate.ExecutablePath.EndsWith("Example.exe", StringComparison.OrdinalIgnoreCase)
            && candidate.Confidence == GameCandidateConfidence.High);

        var missingContext = CreateContext(Path.Combine(directory.Path, "removed-disk"));
        Assert.Empty(await adapter.ScanAsync(missingContext, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShortcutAdapterPreservesArgumentsAndWorkingDirectory()
    {
        using var directory = TemporaryDirectory.Create();
        var gameDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "Games", "Example"));
        var executablePath = Path.Combine(gameDirectory.FullName, "Example.exe");
        await File.WriteAllBytesAsync(
            executablePath,
            new byte[1024 * 1024],
            TestContext.Current.CancellationToken);
        var shortcutPath = Path.Combine(directory.Path, "Example.lnk");
        var adapter = new ShortcutGameSourceAdapter(
            new StubShortcutInspector(shortcutPath, executablePath, gameDirectory.FullName),
            new StubShortcutLocator(shortcutPath),
            NullLogger<ShortcutGameSourceAdapter>.Instance);

        var candidates = await adapter.ScanAsync(
            CreateContext(directory.Path),
            null,
            TestContext.Current.CancellationToken);

        var candidate = Assert.Single(candidates);
        Assert.Equal("--windowed", candidate.Arguments);
        Assert.Equal(gameDirectory.FullName, candidate.WorkingDirectory);
        Assert.Equal(GameCandidateConfidence.High, new GameCandidateScorer().Score(candidate, DateTimeOffset.UtcNow).Confidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SteamManifestIsRecognizedFromSteamAppsAndCommonRoots(bool useCommonRoot)
    {
        using var directory = TemporaryDirectory.Create();
        var steamApps = Directory.CreateDirectory(Path.Combine(directory.Path, "SteamLibrary", "steamapps"));
        await File.WriteAllTextAsync(
            Path.Combine(steamApps.FullName, "appmanifest_100.acf"),
            "\"AppState\" { \"appid\" \"100\" \"name\" \"Example Game\" \"installdir\" \"Example\" }",
            TestContext.Current.CancellationToken);
        var installRoot = Directory.CreateDirectory(Path.Combine(steamApps.FullName, "common", "Example"));
        await File.WriteAllBytesAsync(
            Path.Combine(installRoot.FullName, "ExampleGame.exe"),
            new byte[1024 * 1024],
            TestContext.Current.CancellationToken);

        var adapter = new SteamGameSourceAdapter(NullLogger<SteamGameSourceAdapter>.Instance);
        var rootPath = useCommonRoot
            ? Path.Combine(steamApps.FullName, "common")
            : steamApps.FullName;
        var candidates = await adapter.ScanAsync(
            CreateContext(rootPath),
            null,
            TestContext.Current.CancellationToken);

        var candidate = Assert.Single(candidates);
        Assert.Equal(GameCandidateSource.Steam, candidate.Source);
        Assert.Equal("100", candidate.SourceGameId);
        Assert.Equal(GameCandidateConfidence.High, new GameCandidateScorer().Score(candidate, DateTimeOffset.UtcNow).Confidence);
    }

    [Fact]
    public async Task SteamManifestDoesNotFollowLibrariesOutsideConfiguredRoots()
    {
        using var directory = TemporaryDirectory.Create();
        var configuredSteamApps = Directory.CreateDirectory(Path.Combine(directory.Path, "Configured", "steamapps"));
        var externalSteamApps = Directory.CreateDirectory(Path.Combine(directory.Path, "External", "steamapps"));
        var externalLibrary = Path.GetDirectoryName(externalSteamApps.FullName)!;
        await File.WriteAllTextAsync(
            Path.Combine(configuredSteamApps.FullName, "libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{externalLibrary.Replace("\\", "\\\\")}\" }} }}",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(externalSteamApps.FullName, "appmanifest_100.acf"),
            "\"AppState\" { \"appid\" \"100\" \"name\" \"External Game\" \"installdir\" \"External\" }",
            TestContext.Current.CancellationToken);
        var externalInstall = Directory.CreateDirectory(Path.Combine(externalSteamApps.FullName, "common", "External"));
        await File.WriteAllBytesAsync(
            Path.Combine(externalInstall.FullName, "ExternalGame.exe"),
            new byte[1024 * 1024],
            TestContext.Current.CancellationToken);

        var adapter = new SteamGameSourceAdapter(NullLogger<SteamGameSourceAdapter>.Instance);
        var candidates = await adapter.ScanAsync(
            CreateContext(Path.Combine(directory.Path, "Configured")),
            null,
            TestContext.Current.CancellationToken);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task ShortcutAdapterSkipsTargetsOutsideConfiguredRoots()
    {
        using var directory = TemporaryDirectory.Create();
        var root = Directory.CreateDirectory(Path.Combine(directory.Path, "Games"));
        var external = Directory.CreateDirectory(Path.Combine(directory.Path, "Desktop"));
        var executablePath = Path.Combine(external.FullName, "Tool.exe");
        await File.WriteAllBytesAsync(executablePath, new byte[1024], TestContext.Current.CancellationToken);
        var shortcutPath = Path.Combine(directory.Path, "Tool.lnk");
        var adapter = new ShortcutGameSourceAdapter(
            new StubShortcutInspector(shortcutPath, executablePath, external.FullName),
            new StubShortcutLocator(shortcutPath),
            NullLogger<ShortcutGameSourceAdapter>.Instance);

        var candidates = await adapter.ScanAsync(
            CreateContext(root.FullName),
            null,
            TestContext.Current.CancellationToken);

        Assert.Empty(candidates);
    }

    private static GameScanContext CreateContext(string rootPath)
    {
        var root = new ScanRoot(
            Guid.NewGuid(),
            "test-volume",
            rootPath,
            string.Empty,
            ScanMode.Quick,
            true,
            true,
            null,
            null);
        return new GameScanContext(
            ScanMode.Quick,
            [root],
            [],
            new Dictionary<string, GameCandidate>(StringComparer.OrdinalIgnoreCase),
            new ScanPauseController());
    }

    private sealed class StubShortcutLocator(string path) : IShortcutSourceLocator
    {
        public Task<IReadOnlyList<string>> FindAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>([path]);
        }
    }

    private sealed class StubShortcutInspector(
        string shortcutPath,
        string executablePath,
        string workingDirectory) : ILocalGameFileInspector
    {
        public Task<LocalGameFileInspection> InspectAsync(
            string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(shortcutPath, path);
            return Task.FromResult(
                new LocalGameFileInspection(
                    shortcutPath,
                    executablePath,
                    "Example",
                    "--windowed",
                    workingDirectory,
                    GameSourceType.ManualShortcut,
                    LaunchKind.Shortcut,
                    executablePath));
        }

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(File.Exists(path));
        }
    }
}
