using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>BR-01 PER_AGV and resource-locking behaviour against the real PostgreSQL schema.</summary>
[Collection("Database")]
public class ConcurrencyTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Database_itself_rejects_two_active_tasks_for_one_agv()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        var agv = await h.AgvAsync("AGV1");
        var a1 = await h.LocationAsync("A1");

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => h.Db(async db =>
        {
            foreach (var key in new[] { "k1", "k2" })
            {
                var t = TransportTask.Create(key, ProcessFlow.Bp01, RackKind.Full, agv.Id, a1.Id, "test", h.Clock.UtcNow);
                t.TransitionTo(TaskState.Validating, "v", "t", h.Clock.UtcNow);
                t.TransitionTo(TaskState.Dispatching, "d", "t", h.Clock.UtcNow);
                db.Tasks.Add(t);
            }
            await db.SaveChangesAsync();
            return 0;
        }));
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
        Assert.Contains("one_active_per_agv", pg.ConstraintName);
    }

    [Fact]
    public async Task Queued_and_terminal_tasks_do_not_count_against_the_agv_limit()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        var agv = await h.AgvAsync("AGV1");
        var a1 = await h.LocationAsync("A1");

        await h.Db(async db =>
        {
            var queued = TransportTask.Create("q", ProcessFlow.Bp01, RackKind.Full, agv.Id, a1.Id, "t", h.Clock.UtcNow);
            queued.TransitionTo(TaskState.Validating, "v", "t", h.Clock.UtcNow);
            queued.TransitionTo(TaskState.Queued, "q", "t", h.Clock.UtcNow);
            var done = TransportTask.Create("d", ProcessFlow.Bp01, RackKind.Full, agv.Id, a1.Id, "t", h.Clock.UtcNow);
            done.TransitionTo(TaskState.Cancelled, "c", "t", h.Clock.UtcNow);
            var active = TransportTask.Create("a", ProcessFlow.Bp01, RackKind.Full, agv.Id, a1.Id, "t", h.Clock.UtcNow);
            active.TransitionTo(TaskState.Validating, "v", "t", h.Clock.UtcNow);
            active.TransitionTo(TaskState.Dispatching, "d", "t", h.Clock.UtcNow);
            db.Tasks.AddRange(queued, done, active);
            await db.SaveChangesAsync();
            return 0;
        });
        Assert.Equal(1, await h.ActiveTaskCountAsync("AGV1"));
    }

    [Fact]
    public async Task Parallel_task_creation_activates_exactly_one_task_per_agv_and_reserves_distinct_racks()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 10);
        await h.StockAsync("F1", RackKind.Full, 3);
        await h.StockAsync("F2", RackKind.Full, 3);

        // 6 simultaneous BP-05 requests (all AGV1) racing for 6 racks.
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => h.CreateTaskAsync("BP05", "F5", $"par-{i}")));

        Assert.All(results, r => Assert.True(r.Created));
        Assert.Equal(1, await h.ActiveTaskCountAsync("AGV1"));

        var tasks = await h.Db(db => db.Tasks.AsNoTracking().ToListAsync());
        Assert.Equal(6, tasks.Count);
        Assert.Equal(1, tasks.Count(t => t.State == TaskState.Accepted));
        Assert.Equal(5, tasks.Count(t => t.State == TaskState.Queued));
        Assert.Equal(6, tasks.Select(t => t.RackId).Distinct().Count()); // no rack used twice
        Assert.Equal(1, h.Rcs.CreatedTaskCount);                          // only the active one reached RCS
    }

    [Fact]
    public async Task Two_agvs_run_in_parallel()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        await h.StockAsync("A1", RackKind.Full, 1);   // BP-01 source (AGV1)
        await h.StockAsync("F3", RackKind.Empty, 1);  // BP-02 source (AGV2)

        var t1 = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var t2 = (await h.CreateTaskAsync("BP02", "A2")).Task;

        Assert.Equal("Accepted", t1.State);
        Assert.Equal("Accepted", t2.State);
        Assert.NotEqual(t1.AgvId, t2.AgvId);
        Assert.Equal(1, await h.ActiveTaskCountAsync("AGV1"));
        Assert.Equal(1, await h.ActiveTaskCountAsync("AGV2"));
        Assert.Equal(2, h.Rcs.CreatedTaskCount);
    }

    [Fact]
    public async Task Each_agv_keeps_its_own_queue()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 5);
        await h.StockAsync("F1", RackKind.Full, 2);
        await h.StockAsync("F3", RackKind.Empty, 1);

        var full1 = (await h.CreateTaskAsync("BP05", "F5")).Task;
        var full2 = (await h.CreateTaskAsync("BP05", "F5")).Task;
        var empty = (await h.CreateTaskAsync("BP02", "A1")).Task;

        Assert.Equal("Accepted", full1.State);
        Assert.Equal("Queued", full2.State);   // AGV1 busy
        Assert.Equal("Accepted", empty.State); // AGV2 is not blocked by AGV1's queue
    }

    [Fact]
    public async Task Last_free_destination_slot_is_given_to_only_one_of_two_concurrent_reservations()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(stationCapacity: 1);
        var agv = await h.AgvAsync("AGV1");
        var f5 = await h.LocationAsync("F5");
        await h.SetCapacityAsync("F5", 1);

        // Two independent tasks that each want a slot at F5 (cap 1) at the same time.
        Guid[] ids = await h.Db(async db =>
        {
            var list = new List<TransportTask>();
            foreach (var key in new[] { "d1", "d2" })
            {
                var t = TransportTask.Create(key, ProcessFlow.Bp05, RackKind.Full, agv.Id, f5.Id, "t", h.Clock.UtcNow);
                db.Tasks.Add(t); list.Add(t);
            }
            await db.SaveChangesAsync();
            return list.Select(t => t.Id).ToArray();
        });

        async Task<bool> TryReserve(Guid id) => await h.Scoped(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var tasks = sp.GetRequiredService<ITransportTaskRepository>();
            var inventory = sp.GetRequiredService<IInventoryService>();
            var flows = sp.GetRequiredService<WCS.Application.Flows.IFlowCatalog>();
            return await uow.ExecuteAsync(async ct =>
            {
                var task = (await tasks.GetAsync(id, ct))!;
                return await inventory.ReserveDestinationAsync(task, flows.Get(task.Flow), ct) is not null;
            });
        });

        var outcomes = await Task.WhenAll(ids.Select(TryReserve));
        Assert.Equal(1, outcomes.Count(x => x));
        Assert.Single((await h.ActiveReservationsAsync()), r => r.Kind == ReservationKind.DestinationLocation);
    }
}
