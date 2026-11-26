using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Routing;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-042 (OQ-42-08): a rack of a transport order that is not finished belongs to that order, also while it stands at a buffer between two legs. No other order, task or
/// manual correction takes it; it is released by completion, by cancellation, or (after a failure that followed a movement) by an Admin. Real PostgreSQL, SIMULATOR only.
/// </summary>
[Collection("Database")]
public class Cr042RackOwnershipTests(DatabaseFixture fixture)
{
    private static async Task<string> CodeOf(Func<Task> f) => (await Assert.ThrowsAsync<AppException>(f)).Code;
    private static Task<OrderDto> ReleaseAsync(RoutingKit kit, Guid order, string reason = "checked on site") => kit.With(sp => sp.GetRequiredService<IRouteEngine>().ReleaseRacksAsync(order, reason, "admin"));
    private static Task<OrderDto> CancelAsync(RoutingKit kit, Guid order) => kit.With(sp => sp.GetRequiredService<IRouteEngine>().CancelAsync(order, "cancel", "admin"));
    private static Task<Guid> DeleteRackAsync(RoutingKit kit, Guid rack) => kit.With(sp => sp.GetRequiredService<RackAdminService>().DeleteAsync(rack, "count", "admin"));

    private static async Task<Guid> PublishedAsync(RoutingKit kit, string code, string trigger, VersionDefinition definition, RequestType type = RequestType.DeliveryRequest)
    {
        var (_, v) = await kit.CreateProcessAsync(code, type, RackKind.Full, [trigger], definition);
        await kit.PublishAsync(v);
        return v;
    }

    private static VersionDefinition TakeFrom(string destination) => new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, destination)]), Done()]);

    private static async Task<OrderDto> StartAsync(RoutingKit kit, string key, string at, string code, RequestType type = RequestType.DeliveryRequest) =>
        (await kit.StartAsync(key, at, type, RackKind.Full, code)).Order;

    private static async Task MoveCurrentLegAsync(RoutingKit kit, Guid order)
    {
        var leg = (await kit.OrderAsync(order)).Legs.Single(l => !l.IsTerminal);
        await kit.PickAsync(leg.Id); await kit.TickAsync(); await kit.DropAsync(leg.Id); await kit.TickAsync();
    }

    /// <summary>The order has stored its rack at the buffer F1 and waits there (C1 is full).</summary>
    private static async Task<(RoutingKit Kit, Guid Order, Guid Rack)> AtBufferAsync(DatabaseFixture f)
    {
        var kit = await RoutingKit.CreateAsync(f);
        await PublishedAsync(kit, "T-BUF", "A1", MultiLeg("C1", "F1"));
        await PublishedAsync(kit, "T-TAKE", "F1", TakeFrom("F2"), RequestType.PickupRequest);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("C1", RackKind.Full, 1);
        var order = await StartAsync(kit, "buf", "A1", "T-BUF");
        var rack = (await kit.RackAtAsync("A1"))!.Id;
        await MoveCurrentLegAsync(kit, order.Id);
        Assert.Equal((OrderState.Waiting, WaitKind.AtBuffer), ((await kit.OrderAsync(order.Id)).State, (await kit.OrderAsync(order.Id)).WaitKind));
        return (kit, order.Id, rack);
    }

    private static async Task<OrderDto> OtherAsync(RoutingKit kit, string key = "take") => await StartAsync(kit, key, "F1", "T-TAKE", RequestType.PickupRequest);

    private static async Task AssertNotTakenAsync(RoutingKit kit, Guid other)
    {
        await kit.TickAsync(); await kit.TickAsync();
        var order = await kit.OrderAsync(other);
        Assert.Empty(order.Legs);
        Assert.Equal((OrderState.Waiting, WaitKind.SourceUnavailable), (order.State, order.WaitKind));
    }

    // ================================================================== no second taker

    [Fact]
    public async Task Another_order_cannot_take_a_rack_stored_at_a_buffer_and_the_owner_finishes_with_it()
    {
        var (kit, order, rack) = await AtBufferAsync(fixture);
        await using var _ = kit;
        var other = await OtherAsync(kit);
        await AssertNotTakenAsync(kit, other.Id);

        await kit.H.Db(async db => { var c1 = (await db.Locations.SingleAsync(l => l.Code == "C1")).Id; db.Racks.RemoveRange(await db.Racks.Where(r => r.CurrentLocationId == c1).ToListAsync()); await db.SaveChangesAsync(); return 0; });
        await kit.TickAsync(); await kit.TickAsync();
        var retrieval = (await kit.OrderAsync(order)).Legs.Single(l => !l.IsTerminal);
        Assert.Equal((rack, "F1", "C1"), (retrieval.RackId!.Value, retrieval.SourceLocationCode, retrieval.DestinationLocationCode));
        await MoveCurrentLegAsync(kit, order);
        Assert.Equal(OrderState.Completed, (await kit.OrderAsync(order)).State);
        await AssertNotTakenAsync(kit, other.Id);   // the rack has left the buffer: there is nothing to take there
    }

    [Fact]
    public async Task The_other_order_gets_a_free_rack_of_the_same_buffer_never_the_owned_one()
    {
        var (kit, _, rack) = await AtBufferAsync(fixture);
        await using var _k = kit;
        await kit.H.StockAsync("F1", RackKind.Full, 1);
        var leg = Assert.Single((await kit.OrderAsync((await OtherAsync(kit)).Id)).Legs);
        Assert.NotEqual(rack, leg.RackId);
    }

    [Fact]
    public async Task Requests_that_arrive_while_the_rack_is_being_stored_never_take_it()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        await PublishedAsync(kit, "T-BUF", "A1", MultiLeg("C1", "F1"));
        await PublishedAsync(kit, "T-TAKE", "F1", TakeFrom("F2"), RequestType.PickupRequest);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("C1", RackKind.Full, 1);
        var order = await StartAsync(kit, "buf", "A1", "T-BUF");
        var rack = (await kit.RackAtAsync("A1"))!.Id;
        var leg = (await kit.OrderAsync(order.Id)).Legs.Single();
        await kit.PickAsync(leg.Id); await kit.TickAsync();

        var requests = Enumerable.Range(0, 5).Select(i => (Task)OtherAsync(kit, $"race-{i}")).ToList();
        await Task.WhenAll(requests.Append(kit.DropAsync(leg.Id)));
        for (var i = 0; i < 3; i++) await kit.TickAsync();

        var takers = await kit.H.Db(d => d.TransportLegs.Where(l => l.RackId == rack && l.OrderId != order.Id).CountAsync());
        Assert.Equal(0, takers);
    }

    [Fact]
    public async Task The_Phase_1_task_engine_does_not_take_an_owned_rack_either()
    {
        var (kit, order, rack) = await AtBufferAsync(fixture);
        await using var _ = kit;
        var refused = (await kit.H.CreateTaskAsync("Bp05", "F5", "legacy-1")).Task;
        var failed = await kit.H.GetTaskAsync(refused.Id);
        Assert.Null(failed.RackId);   // the approved behaviour without a source: NO_SOURCE_AVAILABLE
        Assert.Equal(WCS.Domain.Tasks.TaskState.Failed, failed.State);
        Assert.DoesNotContain(await kit.H.ActiveReservationsAsync(), r => r.RackId == rack);

        await CancelAsync(kit, order);   // a confirmed release
        var accepted = (await kit.H.CreateTaskAsync("Bp05", "F5", "legacy-2")).Task;
        Assert.Equal(rack, (await kit.H.GetTaskAsync(accepted.Id)).RackId);
    }

    [Theory]
    [InlineData(OrderState.Waiting)]
    [InlineData(OrderState.InProgress)]
    [InlineData(OrderState.ReconciliationRequired)]
    public async Task Every_unfinished_state_keeps_the_rack_also_after_a_restart(OrderState state)
    {
        var (kit, order, rack) = await AtBufferAsync(fixture);
        await using var _ = kit;
        await kit.H.Db(async db =>
        {
            var o = await db.TransportOrders.Include(x => x.Legs).SingleAsync(x => x.Id == order);
            o.TransitionTo(state, "test", WaitKind.AtBuffer, kit.H.Clock.UtcNow); await db.SaveChangesAsync(); return 0;
        });
        await kit.H.RestartAsync();   // ownership is read from the database: nothing is held in memory
        var other = await OtherAsync(kit);
        await AssertNotTakenAsync(kit, other.Id);
        Assert.Equal("RACK_IN_USE_BY_ORDER", await CodeOf(() => DeleteRackAsync(kit, rack)));
        Assert.Equal("ORDER_HOLDS_NO_RACK", await CodeOf(() => ReleaseAsync(kit, order)));   // the release is for failed orders only
    }

    [Fact]
    public async Task Cancelling_the_order_releases_the_rack()
    {
        var (kit, order, rack) = await AtBufferAsync(fixture);
        await using var _ = kit;
        var other = await OtherAsync(kit);
        await CancelAsync(kit, order);
        await kit.TickAsync(); await kit.TickAsync();
        Assert.Equal(rack, Assert.Single((await kit.OrderAsync(other.Id)).Legs).RackId);
    }

    // ================================================================== the owner itself

    [Fact]
    public async Task The_owner_sees_its_own_rack_in_a_waiting_condition_and_in_a_candidate_source()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        await PublishedAsync(kit, "T-OWN", "A1", new VersionDefinition("s1",
        [
            Move("s1", 1, "BufferStorage", "Request", "Candidates", "w1", [Dest(1, "F1")]),
            new StepDefinition("w1", 2, "Waiting", null, null, null, "s2", null, null, null, [Cond("RackPresent", null, "F1")], null, null, null),
            Move("s2", 3, "BufferRetrieval", "Candidates", "Candidates", "done", [Src(1, "F1"), Dest(1, "C1")]),
            Done(),
        ]));
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var order = await StartAsync(kit, "own", "A1", "T-OWN");
        var rack = (await kit.RackAtAsync("A1"))!.Id;
        await MoveCurrentLegAsync(kit, order.Id);
        await kit.TickAsync();
        Assert.Equal(rack, (await kit.OrderAsync(order.Id)).Legs.Single(l => !l.IsTerminal).RackId);
    }

    // ================================================================== failure

    private static async Task<(RoutingKit Kit, Guid Order, Guid Rack)> FailedWithRackAsync(DatabaseFixture f)
    {
        var kit = await RoutingKit.CreateAsync(f);
        await PublishedAsync(kit, "T-FAIL", "A1", new VersionDefinition("s1",
        [
            Move("s1", 1, "BufferStorage", "Request", "Candidates", "s2", [Dest(1, "F1")]),
            Move("s2", 2, "BufferRetrieval", "PreviousDestination", "Candidates", "done", [Dest(1, "C1")], wait: "Fail"),
            Done(),
        ]));
        await PublishedAsync(kit, "T-TAKE", "F1", TakeFrom("F2"), RequestType.PickupRequest);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("C1", RackKind.Full, 1);
        var order = await StartAsync(kit, "fail", "A1", "T-FAIL");
        var rack = (await kit.RackAtAsync("A1"))!.Id;
        await MoveCurrentLegAsync(kit, order.Id);
        var failed = await kit.OrderAsync(order.Id);
        Assert.Equal((OrderState.Failed, true), (failed.State, failed.RacksHeld));
        return (kit, order.Id, rack);
    }

    [Fact]
    public async Task A_failed_order_keeps_the_rack_it_moved_until_an_Admin_releases_it()
    {
        var (kit, order, rack) = await FailedWithRackAsync(fixture);
        await using var _ = kit;
        var other = await OtherAsync(kit);
        await AssertNotTakenAsync(kit, other.Id);
        Assert.Equal("RACK_IN_USE_BY_ORDER", await CodeOf(() => DeleteRackAsync(kit, rack)));
        Assert.Equal("RACK_IN_USE_BY_ORDER", await CodeOf(() => kit.With(sp => sp.GetRequiredService<IInventoryService>().AdjustStockAsync("F1", RackKind.Full, 0, "op", "count"))));
        await CancelAsync(kit, order);   // cancelling a finished order changes nothing
        await AssertNotTakenAsync(kit, other.Id);
        Assert.Equal("REASON_REQUIRED", await CodeOf(() => ReleaseAsync(kit, order, "  ")));

        var released = await ReleaseAsync(kit, order);
        Assert.Equal(("Failed", false), (released.State, released.RacksHeld));
        Assert.Equal(1, await kit.H.Db(d => d.RoutingEvents.CountAsync(e => e.OrderId == order && e.Kind == "ORDER_RACKS_RELEASED")));
        await kit.TickAsync(); await kit.TickAsync();
        Assert.Equal(rack, Assert.Single((await kit.OrderAsync(other.Id)).Legs).RackId);
        Assert.Equal("ORDER_HOLDS_NO_RACK", await CodeOf(() => ReleaseAsync(kit, order)));
    }

    [Fact]
    public async Task A_rack_without_a_confirmed_position_is_not_released()
    {
        var (kit, order, rack) = await FailedWithRackAsync(fixture);
        await using var _ = kit;
        await kit.H.Db(async db => { (await db.Racks.SingleAsync(r => r.Id == rack)).MarkUnknown(kit.H.Clock.UtcNow); await db.SaveChangesAsync(); return 0; });
        Assert.Equal("RACK_POSITION_NOT_CONFIRMED", await CodeOf(() => ReleaseAsync(kit, order)));
        Assert.True((await kit.OrderAsync(order)).RacksHeld);
    }

    private sealed class RejectingDispatcher : ILegDispatcher
    {
        public Task<LegDispatchResult> DispatchAsync(LegDispatchRequest request, CancellationToken ct = default) => Task.FromResult(new LegDispatchResult(LegDispatchOutcome.Rejected, null, "no"));
        public Task<LegDispatchResult> ContinueAsync(string requestCode, string? agvTaskCode, string destinationLocationCode, CancellationToken ct = default) => DispatchAsync(null!, ct);
    }

    [Fact]
    public async Task An_order_that_failed_before_anything_moved_holds_nothing()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture, more: s => s.AddSingleton<ILegDispatcher, RejectingDispatcher>());
        await PublishedAsync(kit, "T-REJ", "A1", TakeFrom("C1"), RequestType.PickupRequest);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var order = await StartAsync(kit, "rej", "A1", "T-REJ", RequestType.PickupRequest);
        await kit.TickAsync();
        var failed = await kit.OrderAsync(order.Id);
        Assert.Equal((OrderState.Failed, false), (failed.State, failed.RacksHeld));
        await DeleteRackAsync(kit, (await kit.RackAtAsync("A1"))!.Id);   // free again: nothing was sent
    }

    // ================================================================== a station signal and an owned rack

    [Fact]
    public async Task An_owned_rack_is_still_recorded_where_it_stands_but_is_offered_to_nobody()
    {
        var (kit, _, _) = await AtBufferAsync(fixture);
        await using var _k = kit;
        var f1 = (await kit.H.LocationAsync("F1")).Id;
        var (recorded, offered) = await kit.With(async sp =>
        {
            var racks = sp.GetRequiredService<IRackRepository>();
            return (await racks.IsRackRecordedAsync(f1, RackStatus.Full, false), await racks.FindUnreservedAsync(f1, RackStatus.Full));
        });
        Assert.True(recorded);   // so a station that reports it is not an inventory disagreement
        Assert.Null(offered);
    }

    // ================================================================== HTTP

    [Fact]
    public async Task Only_an_Admin_releases_the_racks_over_HTTP_and_the_release_is_audited()
    {
        var (kit, order, _) = await FailedWithRackAsync(fixture);
        await using var _k = kit;
        await using var f = new DbApiFactory(fixture);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator));
            await db.SaveChangesAsync();
        }
        async Task<HttpClient> As(string user) { var c = f.CreateClient().WithClientHeader(); Assert.Equal(200, await c.LoginAsync(user)); return c; }
        var path = $"/api/routing/orders/{order}/release-racks";
        Assert.Equal(403, (await (await As("op1")).SendJsonAsync(HttpMethod.Post, path, new { reason = "x" })).Status);
        var ok = await (await As("admin1")).SendJsonAsync(HttpMethod.Post, path, new { reason = "checked on site" });
        Assert.Equal(200, ok.Status);
        Assert.False(ok.Root.GetProperty("data").GetProperty("racksHeld").GetBoolean());
        Assert.True(await kit.H.Db(d => d.AuditLog.AnyAsync(a => a.Action == "ROUTING_ORDER_RELEASE_RACKS")));
    }

    // ================================================================== migration

    [Fact]
    public async Task The_hold_migration_refuses_to_go_down_while_a_rack_is_held_and_returns_up()
    {
        var scratch = new DatabaseFixture();
        await scratch.InitializeAsync();
        try
        {
            const string before = "20261126031415_AddVersionWithdrawnByAdmin";
            var cs = scratch.ConnectionString;
            WcsDbContext Context() => new(new DbContextOptionsBuilder<WcsDbContext>().UseNpgsql(cs).Options);
            async Task<string?> Scalar(string sql) { await using var c = new NpgsqlConnection(cs); await c.OpenAsync(); await using var cmd = new NpgsqlCommand(sql, c); return (await cmd.ExecuteScalarAsync())?.ToString(); }
            const string column = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'transport_orders' AND column_name = 'RacksHeld'";
            var (kit, order, _) = await FailedWithRackAsync(scratch);
            await using (kit)
            {
                var refused = await Assert.ThrowsAnyAsync<Exception>(() => Context().GetService<IMigrator>().MigrateAsync(before));
                Assert.Contains("ROLLBACK_REFUSED", refused.ToString());
                await ReleaseAsync(kit, order);
            }
            await Context().GetService<IMigrator>().MigrateAsync(before);
            Assert.Equal("0", await Scalar(column));
            await Context().Database.MigrateAsync();
            Assert.Equal("1", await Scalar(column));
        }
        finally { await scratch.DisposeAsync(); }
    }
}
