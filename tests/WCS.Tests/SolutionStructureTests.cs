namespace WCS.Tests;

/// <summary>
/// Foundation smoke tests only: verify that the test project is wired to the
/// solution layers. No business (AGV, Task, Inventory, PLC/RCS) tests here.
/// </summary>
public class SolutionStructureTests
{
    [Theory]
    [InlineData(typeof(WCS.Domain.Class1))]
    [InlineData(typeof(WCS.Application.Class1))]
    [InlineData(typeof(WCS.Infrastructure.Class1))]
    [InlineData(typeof(WCS.Runtime.Class1))]
    public void Layer_assembly_is_referenced(Type marker)
    {
        Assert.NotNull(marker.Assembly);
    }
}
