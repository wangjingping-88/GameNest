namespace GameNest.Application;

public interface ILocalGameFileInspector
{
    Task<LocalGameFileInspection> InspectAsync(string path, CancellationToken cancellationToken);

    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken);
}
