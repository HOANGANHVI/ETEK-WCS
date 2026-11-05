using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Tasks;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>BP-01, 02, 03, 05, 06 end-to-end against the simulators (BP-04 / A3-A4 / D1-D2 are NOT implemented).</summary>
[Collection("Database")]
public class ProcessFlowTests(DatabaseFixture fixture)
{
    // ------------------------------------------------------------------------------------------------- BP-01
    [Fact]
    public async Task Bp01_full_rack_from_A1_goes_to_B1_without_binding_the_rack()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        await h.StockAsync("A1", RackKind.Full, 1);

        var task = (await h.CreateTaskAsync("BP01", "A1")).Task;
        Assert.Equal("Accepted", task.State);
        Assert.Equal(1, h.Rcs.CreateCalls);

        await h.AdvanceUntilAsync(task.Id, TaskState.Completed);

        var done = await h.GetTaskAsync(task.Id);
        Assert.Equal("B1", (await h.LocationAsync("B1")).Code);
        Assert.Equal((await h.LocationAsync("B1")).Id, done.DestinationLocationId); // first configured destination group
        var rackAtB1 = Assert.Single(await h.RacksAtAsync("B1"));
        Assert.Equal(RackStatus.Full, rackAtB1.Status);
        Assert.Empty(await h.RacksAtAsync("A1"));
        Assert.Empty(await h.ActiveReservationsAsync());

        var tx = await h.TransactionsAsync(task.Id);
        Assert.Equal([InventoryEventType.PickConfirmed, InventoryEventType.DropConfirmed], tx.Select(t => t.EventType));
    }

    [Fact]
    public async Task Bp01_no_bind_rack_at_B1_but_binding_is_allowed_at_F1()
    {
        // B1 (documented "Không bind rack") ignores an external rack id.
        await using (var h = await Harness.CreateAsync(fixture))
        {
            await h.SeedStandardAsync();
            await h.StockAsync("A1", RackKind.Full, 1);
            var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
            await h.AdvanceUntilAsync(t.Id, TaskState.Executing);   // ensure we are past dispatch
            await DriveToDropAsync(h, t.Id);
            var drop = await h.SignalAsync(t.Id, SignalKind.DropConfirmed, "drop-1", externalRackId: "RACK-777");

            Assert.Equal(SignalOutcome.Applied, drop.Outcome);
            var rack = Assert.Single(await h.RacksAtAsync("B1"));
            Assert.Null(rack.ExternalRackId);
            var dropTx = (await h.TransactionsAsync(t.Id)).Single(x => x.EventType == InventoryEventType.DropConfirmed);
            Assert.Equal(BindOutcome.NotBoundByProcess, dropTx.Bind);
        }

        // F1 uses WCS inventory with binding: B1, B2 are full so the destination becomes F1.
        await using (var h = await Harness.CreateAsync(fixture))
        {
            await h.SeedStandardAsync();
            await h.StockAsync("A1", RackKind.Full, 1);
            await h.StockAsync("B1", RackKind.Full, 1);
            await h.StockAsync("B2", RackKind.Full, 1);
            var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
            await DriveToDropAsync(h, t.Id);
            var current = await h.GetTaskAsync(t.Id);
            Assert.Equal((await h.LocationAsync("F1")).Id, current.DestinationLocationId);

            await h.SignalAsync(t.Id, SignalKind.DropConfirmed, "drop-2", externalRackId: "RACK-778");
            var rack = Assert.Single(await h.RacksAtAsync("F1"));
            Assert.Equal("RACK-778", rack.ExternalRackId);
            var dropTx = (await h.TransactionsAsync(t.Id)).Single(x => x.EventType == InventoryEventType.DropConfirmed);
            Assert.Equal(BindOutcome.Bound, dropTx.Bind);
        }
    }

    [Fact]
    public async Task Destination_is_reserved_not_occupied_until_drop_is_confirmed()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        await h.StockAsync("A1", RackKind.Full, 1);

        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        // Creation / dispatch alone never marks anything at the destination.
        Assert.Empty(await h.RacksAtAsync("B1"));
        Assert.Empty(await h.ActiveReservationsAsync().ContinueWith(r => r.Result.Where(x => x.Kind == ReservationKind.DestinationLocation).ToList()));

        await DriveToDropAsync(h, t.Id);
        // After pick: rack IN_TRANSIT, destination only RESERVED.
        var racks = await h.Db(db => db.Racks.AsNoTracking().ToListAsync());
        var rack = Assert.Single(racks);
        Assert.Equal(RackStatus.InTransit, rack.Status);
        Assert.Null(rack.CurrentLocationId);
        Assert.Empty(await h.RacksAtAsync("B1"));
        Assert.Single((await h.ActiveReservationsAsync()), r => r.Kind == ReservationKind.DestinationLocation);

        await h.SignalAsync(t.Id, SignalKind.DropConfirmed);
        Assert.Single(await h.RacksAtAsync("B1"));
    }

    // ------------------------------------------------------------------------------------------------- WAITING_FOR_DESTINATION
    [Fact]
    public async Task Bp01_without_destination_waits_keeps_the_agv_and_resumes_when_space_appears()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 1);
        await h.StockAsync("A1", RackKind.Full, 1);
        await h.StockAsync("A2", RackKind.Full, 1);
        foreach (var l in new[] { "B1", "B2", "F1", "F2", "F5" }) await h.StockAsync(l, RackKind.Full, 1);

        var t1 = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var t2 = (await h.CreateTaskAsync("BP01", "A2")).Task;
        Assert.Equal("Queued", t2.State);

        await DriveToPickAsync(h, t1.Id);
        var waiting = await h.GetTaskAsync(t1.Id);
        Assert.Equal(TaskState.WaitingForDestination, waiting.State);   // not cancelled, not replaced
        Assert.Null(waiting.DestinationLocationId);

        // The AGV stays attached to the waiting task: the queued task must not start.
        await h.TickAsync();
        Assert.Equal(TaskState.Queued, (await h.GetTaskAsync(t2.Id)).State);
        Assert.Equal(1, await h.ActiveTaskCountAsync("AGV1"));
        Assert.Equal(1, h.Rcs.CreatedTaskCount);

        // Space appears at F1 -> the scheduler re-evaluates, reserves and continues the SAME task.
        await h.Scoped(sp => sp.GetRequiredService<IInventoryService>().AdjustStockAsync("F1", RackKind.Full, 0, "operator", "freed"));
        await h.TickAsync();
        var resumed = await h.GetTaskAsync(t1.Id);
        Assert.Equal(TaskState.Executing, resumed.State);
        Assert.Equal((await h.LocationAsync("F1")).Id, resumed.DestinationLocationId);
        Assert.Equal(1, h.Rcs.ContinueCalls);

        await h.AdvanceUntilAsync(t1.Id, TaskState.Completed);
        await h.TickAsync();                                              // AGV1 is free now -> queued task starts
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(t2.Id)).State);
    }

    // ------------------------------------------------------------------------------------------------- BP-02
    [Fact]
    public async Task Bp02_empty_rack_from_B1_is_delivered_to_the_requesting_station_A1()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        await h.StockAsync("B1", RackKind.Empty, 1);

        var t = (await h.CreateTaskAsync("BP02", "A1")).Task;
        Assert.Equal((await h.AgvAsync("AGV2")).Id, t.AgvId);              // empty racks -> AGV2
        Assert.Equal((await h.LocationAsync("B1")).Id, t.SourceLocationId);

        await h.AdvanceUntilAsync(t.Id, TaskState.Completed);
        var rack = Assert.Single(await h.RacksAtAsync("A1"));
        Assert.Equal(RackStatus.Empty, rack.Status);
        Assert.Empty(await h.RacksAtAsync("B1"));
    }

    [Fact]
    public async Task Bp02_falls_back_to_F3_when_B1_and_B2_have_no_empty_rack()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        await h.StockAsync("F3", RackKind.Empty, 1);

        var t = (await h.CreateTaskAsync("BP02", "A2")).Task;
        Assert.Equal((await h.LocationAsync("F3")).Id, t.SourceLocationId);
        await h.AdvanceUntilAsync(t.Id, TaskState.Completed);
        Assert.Single(await h.RacksAtAsync("A2"));
    }

    [Fact]
    public async Task Bp02_without_any_empty_rack_fails_without_inventing_a_source()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();

        var t = (await h.CreateTaskAsync("BP02", "A1")).Task;
        Assert.Equal("Failed", t.State);
        Assert.Equal("NO_SOURCE_AVAILABLE", t.StateReason);
        Assert.Equal(0, h.Rcs.CreateCalls);
        Assert.Empty(await h.ActiveReservationsAsync());
        Assert.Empty(await h.Db(db => db.Racks.ToListAsync()));
    }

    // ------------------------------------------------------------------------------------------------- BP-03
    [Fact]
    public async Task Bp03_empty_rack_from_B1_goes_to_A1_without_binding()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        await h.StockAsync("B1", RackKind.Empty, 1);

        var t = (await h.CreateTaskAsync("BP03", "B1")).Task;
        await DriveToDropAsync(h, t.Id);
        Assert.Equal((await h.LocationAsync("A1")).Id, (await h.GetTaskAsync(t.Id)).DestinationLocationId);

        await h.SignalAsync(t.Id, SignalKind.DropConfirmed, "d", externalRackId: "R-9");
        var rack = Assert.Single(await h.RacksAtAsync("A1"));
        Assert.Null(rack.ExternalRackId);                                   // A1/A2 "Không bind rack"
        Assert.Equal(RackStatus.Empty, rack.Status);
    }

    [Fact]
    public async Task Bp03_uses_F3_when_A1_and_A2_are_occupied()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        await h.StockAsync("B1", RackKind.Empty, 1);
        await h.StockAsync("A1", RackKind.Full, 1);
        await h.StockAsync("A2", RackKind.Full, 1);

        var t = (await h.CreateTaskAsync("BP03", "B1")).Task;
        await DriveToPickAsync(h, t.Id);
        Assert.Equal((await h.LocationAsync("F3")).Id, (await h.GetTaskAsync(t.Id)).DestinationLocationId);
    }

    // ------------------------------------------------------------------------------------------------- BP-05
    [Fact]
    public async Task Bp05_full_rack_from_F1_is_delivered_to_F5()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        await h.StockAsync("F1", RackKind.Full, 1);

        var t = (await h.CreateTaskAsync("BP05", "F5")).Task;
        Assert.Equal((await h.AgvAsync("AGV1")).Id, t.AgvId);
        await h.AdvanceUntilAsync(t.Id, TaskState.Completed);

        var rack = Assert.Single(await h.RacksAtAsync("F5"));
        Assert.Equal(RackStatus.Full, rack.Status);
        Assert.Empty(await h.RacksAtAsync("F1"));
    }

    [Fact]
    public async Task Bp05_without_a_full_rack_in_F1_F2_fails_cleanly()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        var t = (await h.CreateTaskAsync("BP05", "F5")).Task;
        Assert.Equal("Failed", t.State);
        Assert.Equal(0, h.Rcs.CreateCalls);
    }

    // ------------------------------------------------------------------------------------------------- BP-06
    [Fact]
    public async Task Bp06_empty_rack_from_F5_returns_to_A1()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        await h.StockAsync("F5", RackKind.Empty, 1);

        var t = (await h.CreateTaskAsync("BP06", "F5")).Task;
        await h.AdvanceUntilAsync(t.Id, TaskState.Completed);
        Assert.Single(await h.RacksAtAsync("A1"));
        Assert.Empty(await h.RacksAtAsync("F5"));
    }

    // ------------------------------------------------------------------------------------------------- out-of-scope flows
    [Theory]
    [InlineData("BP04", "A1", "FLOW_IN_REVIEW")]
    [InlineData("BP-04", "B1", "FLOW_IN_REVIEW")]
    [InlineData("A3", "A3", "FLOW_NOT_SUPPORTED")]
    [InlineData("D1", "D1", "FLOW_NOT_SUPPORTED")]
    public async Task Review_and_legacy_flows_are_rejected(string flow, string station, string expectedCode)
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        var ex = await Assert.ThrowsAsync<AppException>(() => h.CreateTaskAsync(flow, station));
        Assert.Equal(expectedCode, ex.Code);
        Assert.Empty(await h.Db(db => db.Tasks.ToListAsync()));
    }

    [Theory]
    [InlineData("BP01", "F5")]   // F5 cannot raise BP-01
    [InlineData("BP05", "A1")]
    [InlineData("A3", "A1")]
    public async Task A_station_cannot_raise_a_flow_it_is_not_configured_for(string flow, string station)
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        await Assert.ThrowsAsync<AppException>(() => h.CreateTaskAsync(flow, station));
    }

    // ------------------------------------------------------------------------------------------------- station intake
    [Fact]
    public async Task Station_request_arriving_twice_creates_one_task_and_is_acknowledged()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        await h.StockAsync("A1", RackKind.Full, 1);

        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "plc-seq-42"));
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "plc-seq-42"));
        var handled = await h.Scoped(sp => sp.GetRequiredService<WCS.Application.Services.StationRequestProcessor>().RunOnceAsync());

        Assert.Equal(2, handled);
        Assert.Single(await h.Db(db => db.Tasks.ToListAsync()));
        Assert.Equal(2, h.Stations.Acknowledged.Count);
    }

    // ------------------------------------------------------------------------------------------------- helpers
    private static async Task DriveToPickAsync(Harness h, Guid taskId)
    {
        await h.AdvanceUntilAsync(taskId, TaskState.Executing);
        // Walk the handshake + pick; leaves the task in Continuing/Executing/WaitingForDestination.
        for (var i = 0; i < 6; i++)
        {
            var t = await h.GetTaskAsync(taskId);
            if (t.PickConfirmedAt is not null) return;
            await h.AdvanceAsync(taskId);
        }
        Assert.Fail("Pick was not reached.");
    }

    /// <summary>Drive until the pick is done and the continue call succeeded (task Executing on the drop leg).</summary>
    private static async Task DriveToDropAsync(Harness h, Guid taskId)
    {
        await DriveToPickAsync(h, taskId);
        await h.TickAsync();
        var t = await h.GetTaskAsync(taskId);
        Assert.Equal(TaskState.Executing, t.State);
        Assert.Equal(TaskLeg.Drop, t.CurrentLeg);
        // Satisfy the PLC handshake at the destination if it requires one.
        while ((await h.GetTaskAsync(taskId)) is { State: var s } && s != TaskState.Completed)
        {
            var cur = await h.GetTaskAsync(taskId);
            var dest = await h.Db(db => db.Locations.AsNoTracking().SingleAsync(l => l.Id == cur.DestinationLocationId));
            if (!dest.RequiresPlcPermission || cur.PlcPermissionGranted) return;
            await h.AdvanceAsync(taskId);
        }
    }
}
