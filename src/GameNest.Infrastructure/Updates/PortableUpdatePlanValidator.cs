namespace GameNest.Infrastructure.Updates;

public static class PortableUpdatePlanValidator
{
    public static PortableUpdatePlan Validate(PortableUpdatePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.SchemaVersion != 1 || plan.CurrentProcessId <= 0)
        {
            throw new InvalidDataException("升级计划版本或旧进程标识无效。");
        }

        var targetRoot = NormalizeDirectory(plan.TargetRoot);
        var candidateRoot = NormalizeDirectory(plan.CandidateRoot);
        var rollbackRoot = NormalizeDirectory(plan.RollbackRoot);
        var stagingRoot = NormalizeDirectory(plan.StagingRoot);
        var targetParent = Path.GetDirectoryName(targetRoot)
                           ?? throw new InvalidDataException("升级目标目录没有父目录。");
        if (!Path.GetDirectoryName(candidateRoot)!.Equals(targetParent, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetDirectoryName(rollbackRoot)!.Equals(targetParent, StringComparison.OrdinalIgnoreCase) ||
            Path.GetPathRoot(candidateRoot) != Path.GetPathRoot(targetRoot) ||
            Path.GetPathRoot(rollbackRoot) != Path.GetPathRoot(targetRoot))
        {
            throw new InvalidDataException("升级候选、目标和回滚目录必须位于同一父目录及同一磁盘。");
        }

        if (targetRoot.Equals(candidateRoot, StringComparison.OrdinalIgnoreCase) ||
            targetRoot.Equals(rollbackRoot, StringComparison.OrdinalIgnoreCase) ||
            candidateRoot.Equals(rollbackRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("升级目录彼此重叠。");
        }

        if (!File.Exists(Path.Combine(targetRoot, ".gamenest-portable-root")) ||
            !File.Exists(Path.Combine(candidateRoot, ".gamenest-portable-root")) ||
            !File.Exists(Path.Combine(candidateRoot, "GameNest.App.exe")))
        {
            throw new InvalidDataException("升级目录缺少便携版根标记或主程序。");
        }

        if (Directory.Exists(rollbackRoot))
        {
            throw new InvalidDataException("回滚目录已存在，拒绝覆盖可能保留的旧版本。");
        }

        var databaseFile = Path.GetFullPath(plan.DatabaseFile);
        var backupFile = Path.GetFullPath(plan.DatabaseBackupFile);
        if (!File.Exists(backupFile) ||
            string.IsNullOrWhiteSpace(plan.ExpectedVersion) ||
            !File.Exists(Path.Combine(stagingRoot, "package", "GameNest.App.exe")))
        {
            throw new InvalidDataException("升级计划缺少数据库备份、版本或暂存主程序。");
        }

        return plan with
        {
            TargetRoot = targetRoot,
            CandidateRoot = candidateRoot,
            RollbackRoot = rollbackRoot,
            StagingRoot = stagingRoot,
            HealthFile = Path.GetFullPath(plan.HealthFile),
            FailureFile = Path.GetFullPath(plan.FailureFile),
            DatabaseFile = databaseFile,
            DatabaseBackupFile = backupFile,
        };
    }

    private static string NormalizeDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
