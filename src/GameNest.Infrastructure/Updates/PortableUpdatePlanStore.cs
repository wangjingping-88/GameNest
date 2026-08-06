using System.Text.Json;

namespace GameNest.Infrastructure.Updates;

public static class PortableUpdatePlanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static async Task<PortableUpdatePlan> ReadAsync(
        string planFile,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planFile);
        await using var stream = new FileStream(
            planFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var plan = await JsonSerializer
            .DeserializeAsync<PortableUpdatePlan>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return plan ?? throw new InvalidDataException("升级计划为空。");
    }

    public static async Task WriteAsync(
        string planFile,
        PortableUpdatePlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planFile);
        ArgumentNullException.ThrowIfNull(plan);
        var directory = Path.GetDirectoryName(Path.GetFullPath(planFile))
                        ?? throw new InvalidDataException("无法确定升级计划目录。");
        await Task.Run(() => Directory.CreateDirectory(directory), cancellationToken).ConfigureAwait(false);
        await using var stream = new FileStream(
            planFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, plan, JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
