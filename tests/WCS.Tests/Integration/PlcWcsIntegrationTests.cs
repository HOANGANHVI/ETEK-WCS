using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Plc;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>PLC IO (simulator) feeding the EXISTING task flow through IStationSignalService. The flow itself is not touched.</summary>
[Collection("Database")]
public class PlcWcsIntegrationTests(DatabaseFixture fixture)
{
    private static async Task<bool> WaitFor(Func<bool> cond, int ms = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; await Task.Delay(20); }
        return cond();
    }

    [Fact]
    public async Task An_approved_PLC_request_creates_one_task_through_the_unchanged_flow_and_unmapped_stations_create_none()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 5, stationCapacity: 3);
        await h.StockAsync("A1", RackKind.Full, 1);

        await using var f = new DbApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Plc:SimulatorPollingIntervalMs"] = "40",
            ["Plc:Integration:Enabled"] = "true",
            ["Plc:Integration:StationFlows:0:Station"] = "A1", ["Plc:Integration:StationFlows:0:Signal"] = "Pickup_Request", ["Plc:Integration:StationFlows:0:Flow"] = "BP01",
        })));
        using (var scope = f.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>().SeedAsync();
        var rt = f.Services.GetRequiredService<IPlcRuntime>();
        await rt.StartAsync();
        var sim = f.Services.GetRequiredService<ISimulatorControl>();

        // A1 (mapped): request + ID.  A2 (stocked? no mapping) and A3 (a monitor-only station without any location): requests that must be ignored.
        sim.SetValue("PLC-01", "A1", "Request_ID", PointValue.OfString("REQ-1")); sim.SetValue("PLC-01", "A1", "Pickup_Request", PointValue.OfBool(true));
        sim.SetValue("PLC-01", "A2", "Request_ID", PointValue.OfString("REQ-2")); sim.SetValue("PLC-01", "A2", "Pickup_Request", PointValue.OfBool(true));
        sim.SetValue("PLC-01", "A3", "Request_ID", PointValue.OfString("REQ-3")); sim.SetValue("PLC-01", "A3", "Delivery_Request", PointValue.OfBool(true));
        Assert.True(await WaitFor(() => rt.Handler("A1")!.Read("Pickup_Request")?.Value?.Value is true && rt.Handler("A3")!.Read("Delivery_Request")?.Value?.Value is true));

        using (var scope = f.Services.CreateScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<StationRequestProcessor>().RunOnceAsync());      // only A1 produced a request

        var task = await h.Db(db => db.Tasks.AsNoTracking().SingleAsync());
        Assert.Equal("PLC-01:A1:REQ-1", task.RequestKey);
        Assert.Equal((ProcessFlow.Bp01, "PLC"), (task.Flow, task.RequestedBy));
        Assert.Equal((await h.LocationAsync("A1")).Id, task.RequestLocationId);
        Assert.NotEqual(TaskState.Failed, (await h.GetTaskAsync(task.Id)).State);                                          // the normal flow took over (AGV1 + rack reserved)

        // the same level on the next polls does not create anything; toggling the bit with the SAME id stays idempotent
        using (var scope = f.Services.CreateScope()) Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<StationRequestProcessor>().RunOnceAsync());
        sim.SetValue("PLC-01", "A1", "Pickup_Request", PointValue.OfBool(false));
        Assert.True(await WaitFor(() => rt.Handler("A1")!.Read("Pickup_Request")?.Value?.Value is false));
        using (var scope = f.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<StationRequestProcessor>().RunOnceAsync();
        sim.SetValue("PLC-01", "A1", "Pickup_Request", PointValue.OfBool(true));
        Assert.True(await WaitFor(() => rt.Handler("A1")!.Read("Pickup_Request")?.Value?.Value is true));
        using (var scope = f.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<StationRequestProcessor>().RunOnceAsync();
        Assert.Equal(1, await h.Db(db => db.Tasks.CountAsync()));                                                           // still exactly one task

        // nothing was written to any PLC bit (no approved handshake rule exists): Request_Ack and Transfer_Permit stay false
        Assert.False(rt.Handler("A1")!.Read("Request_Ack")?.Value?.Value is true);
        Assert.False(rt.Handler("A1")!.Read("Transfer_Permit")?.Value?.Value is true);
    }

    [Fact]
    public async Task The_legacy_simulator_remains_the_default_so_existing_flows_and_tests_are_unaffected()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await using var f = new DbApiFactory(fixture);
        Assert.IsType<WCS.Infrastructure.Plc.SimulatedPlcNetwork>(f.Services.GetRequiredService<ISimulatorControl>());
        Assert.IsType<WCS.Infrastructure.Simulation.StationSignalSimulator>(f.Services.GetRequiredService<WCS.Application.Abstractions.IStationSignalService>());
        Assert.False(f.Services.GetRequiredService<IPlcRuntime>().Running);                     // no worker is started by merely building the host in tests
    }
}
