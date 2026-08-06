using GameNest.Domain;

namespace GameNest.Application;

public sealed class GameCandidateGrouper : IGameCandidateGrouper
{
    public IReadOnlyList<GameCandidate> Group(IReadOnlyList<GameCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .GroupBy(static candidate => candidate.GroupKey, StringComparer.OrdinalIgnoreCase)
            .SelectMany(GroupDirectory)
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<GameCandidate> GroupDirectory(IGrouping<string, GameCandidate> group)
    {
        var ordered = group
            .OrderByDescending(static candidate => GetSourcePriority(candidate.Source))
            .ThenByDescending(static candidate => candidate.Score)
            .ThenByDescending(static candidate => GetTitleCloseness(candidate))
            .ThenBy(static candidate => candidate.ExecutablePath.Length)
            .ToArray();
        var primaryId = ordered[0].Id;
        return ordered.Select(candidate => candidate.WithGrouping(group.Key, candidate.Id == primaryId));
    }

    private static int GetSourcePriority(GameCandidateSource source) => source switch
    {
        GameCandidateSource.Steam => 3,
        GameCandidateSource.Shortcut => 2,
        GameCandidateSource.GenericExecutable => 1,
        _ => 0,
    };

    private static int GetTitleCloseness(GameCandidate candidate)
    {
        var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(candidate.InstallRoot));
        var executableName = Path.GetFileNameWithoutExtension(candidate.ExecutablePath);
        if (directoryName.Equals(executableName, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return candidate.Title.Contains(directoryName, StringComparison.OrdinalIgnoreCase)
            || directoryName.Contains(candidate.Title, StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
    }
}
