namespace GameNest.Infrastructure.Updates;

public static class PortableDirectoryTransaction
{
    public static Task ExchangeAsync(
        string targetRoot,
        string candidateRoot,
        string rollbackRoot,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(targetRoot, rollbackRoot);
            try
            {
                Directory.Move(candidateRoot, targetRoot);
            }
            catch
            {
                Directory.Move(rollbackRoot, targetRoot);
                throw;
            }
        }, cancellationToken);

    public static Task RestoreAsync(
        string targetRoot,
        string rollbackRoot,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(rollbackRoot))
            {
                return;
            }

            if (Directory.Exists(targetRoot))
            {
                var failedRoot = targetRoot + ".failed-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                Directory.Move(targetRoot, failedRoot);
            }

            Directory.Move(rollbackRoot, targetRoot);
        }, cancellationToken);
}
public static class PortableInstallWriteProbe
{
    public static Task<bool> CanWriteAsync(string installRoot, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var probe = Path.Combine(installRoot, $".gamenest-update-write-{Guid.NewGuid():N}.tmp");
            try
            {
                using (File.Create(probe, 1, FileOptions.WriteThrough))
                {
                }

                File.Delete(probe);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(probe))
                    {
                        File.Delete(probe);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }, cancellationToken);
}
