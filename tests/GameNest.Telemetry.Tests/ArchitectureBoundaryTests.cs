using GameNest.Telemetry;

namespace GameNest.Telemetry.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void TelemetryDoesNotDependOnUiDatabaseOrInfrastructure()
    {
        var referencedAssemblies = TelemetryAssembly.Instance
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .Where(static name => name is not null)
            .ToArray();

        Assert.DoesNotContain("GameNest.Infrastructure", referencedAssemblies);
        Assert.DoesNotContain("GameNest.App", referencedAssemblies);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", referencedAssemblies);
        Assert.DoesNotContain("Microsoft.WindowsAppSDK", referencedAssemblies);
    }
}
