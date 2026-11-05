namespace WCS.Tests;

/// <summary>Foundation smoke test: the test project is wired to every solution layer.</summary>
public class SolutionStructureTests
{
    [Theory]
    [InlineData(typeof(WCS.Domain.Agvs.Agv))]
    [InlineData(typeof(WCS.Application.Flows.FlowCatalog))]
    [InlineData(typeof(WCS.Infrastructure.IntegrationOptions))]
    [InlineData(typeof(WCS.Runtime.SchedulerHostedService))]
    public void Layer_assembly_is_referenced(Type marker)
    {
        Assert.NotNull(marker.Assembly);
    }
}
