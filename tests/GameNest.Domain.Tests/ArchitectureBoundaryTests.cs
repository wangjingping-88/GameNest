using GameNest.Domain;

namespace GameNest.Domain.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void DomainDoesNotReferenceOuterLayers()
    {
        var referencedAssemblies = DomainAssembly.Instance
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .Where(static name => name is not null)
            .ToArray();

        Assert.DoesNotContain("GameNest.Application", referencedAssemblies);
        Assert.DoesNotContain("GameNest.Infrastructure", referencedAssemblies);
        Assert.DoesNotContain("GameNest.App", referencedAssemblies);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", referencedAssemblies);
        Assert.DoesNotContain("Microsoft.UI.Xaml", referencedAssemblies);
    }
}
