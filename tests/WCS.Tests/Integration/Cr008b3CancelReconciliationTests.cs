using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Tasks;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;
using static WCS.Application.Services.TaskOrchestrator;

namespace WCS.Tests.Integration;

/// <summary>CR-008B-3: CancelPending retry limit (decision D-1), NotFound never = Cancelled (D-9), and the Admin reconciliation API.</summary>
[Collection("Database")]
public class Cr008b3CancelReconciliationTests(DatabaseFixture fixture)
{
    private static async Task<Harness> ReadyAsync(DatabaseFixture f)
    {
        var h = await Harness.CreateAsync(f);
        await h.SeedStandardAsync();
        await h.StockAsync("A1", RackKind.Full, 1);
        h.SchedulerOptions.RcsRetryInitialDelayMs = 1000;
        h.SchedulerOptions.RcsRetryMaxDelayMs = 4000;
        h.SchedulerOptions.RcsMaxAttempts = 4;
        return h;
    }

    /// <summary>An accepted task, cancelled by an operator, RCS unreachable: runs the scheduler until the retry limit is reached.</summary>
    private static async Task<TransportTask> ExhaustedCancelAsync(Harness h)
    {
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(t.Id, "operator stop", "tester"));
        h.Rcs.Unreachable = true;
        for (var i = 0; i < 30; i++) { h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.TickAsync(); }
        return await h.GetTaskAsync(t.Id);
    }

    // ------------------------------------------------------------------------------------------------ D-1
    [Fact]
    public async Task Reaching_the_cancel_retry_limit_fails_the_task_once_and_keeps_every_resource()
    {
        await using var h = await ReadyAsync(fixture);
        var task = await ExhaustedCancelAsync(h);

        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal(RetryLimitReason, task.StateReason);
        Assert.True(task.RequiresReconciliation);
        Assert.False(task.ReconciliationCompleted);
        var limit = Assert.Single(task.History, x => x.EventName == "RCS_CANCEL_RETRY_EXHAUSTED");
        Assert.Contains("4 automatic attempts", limit.Detail);                    // the cause and the time are recorded
        Assert.Contains("not a confirmation", limit.Detail);                      // Failed is explicitly NOT evidence of a cancellation
        Assert.Single(task.History, x => x.EventName == "STATE_CHANGED" && x.ToState == TaskState.Failed);          // moved to Failed exactly once
        Assert.DoesNotContain(task.History, x => x.ToState == TaskState.Cancelled);

        Assert.Single(await h.ActiveReservationsAsync());                         // the rack reservation is kept
        var rack = Assert.Single(await h.RacksAtAsync("A1"));
        Assert.Equal(RackStatus.Full, rack.Status);                               // nothing released, nothing moved
        Assert.Equal(0, await h.ActiveTaskCountAsync("AGV1"));                    // terminal task: AGV1 is no longer occupied (see CR008B-5 analysis: policy D-7 is open)
    }

    [Fact]
    public async Task After_the_limit_no_cancel_or_query_is_sent_and_the_counter_is_not_reset_or_raised()
    {
        await using var h = await ReadyAsync(fixture);
        var task = await ExhaustedCancelAsync(h);
        var calls = h.Rcs.CancelCalls + h.Rcs.QueryCalls;
        var count = task.RcsRetryCount;
        var historyCount = task.History.Count;

        h.Rcs.Unreachable = false;
        for (var i = 0; i < 10; i++) { h.Clock.Advance(TimeSpan.FromMinutes(10)); await h.TickAsync(); }

        var later = await h.GetTaskAsync(task.Id);
        Assert.Equal(calls, h.Rcs.CancelCalls + h.Rcs.QueryCalls);
        Assert.Equal(count, later.RcsRetryCount);
        Assert.Equal(historyCount, later.History.Count);                          // no duplicate history either
    }

    [Fact]
    public async Task The_failed_and_reconciliation_state_survives_a_backend_restart()
    {
        await using var h = await ReadyAsync(fixture);
        var task = await ExhaustedCancelAsync(h);
        await h.RestartAsync();

        var calls = h.Rcs.CancelCalls + h.Rcs.QueryCalls;
        for (var i = 0; i < 5; i++) { h.Clock.Advance(TimeSpan.FromMinutes(10)); await h.TickAsync(); }

        var after = await h.GetTaskAsync(task.Id);
        Assert.Equal(TaskState.Failed, after.State);
        Assert.True(after.RequiresReconciliation);
        Assert.Equal(task.History.Count, after.History.Count);
        Assert.Equal(calls, h.Rcs.CancelCalls + h.Rcs.QueryCalls);                // the new process sends nothing
        Assert.Single(await h.ActiveReservationsAsync());
    }

    // ------------------------------------------------------------------------------------------------ Admin reconciliation (API)
    private sealed record Api(HttpClient Admin, HttpClient Operator);

    private static async Task<Api> ApiAsync(DbApiFactory f)
    {
        using (var scope = f.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            ctx.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); ctx.Users.Add(TestUsers.Make("op1", UserRole.Operator));
            await ctx.SaveChangesAsync();
        }
        var admin = f.CreateClient().WithClientHeader(); Assert.Equal(200, await admin.LoginAsync("admin1"));
        var op = f.CreateClient().WithClientHeader(); Assert.Equal(200, await op.LoginAsync("op1"));
        return new Api(admin, op);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task Admin_sees_the_task_that_needs_reconciliation_with_retry_reservation_and_rack_details()
    {
        await using var h = await ReadyAsync(fixture);
        var task = await ExhaustedCancelAsync(h);
        await using var f = new DbApiFactory(fixture);
        var api = await ApiAsync(f);

        var list = await DataAsync(await api.Admin.GetAsync("/api/tasks/reconciliation"));
        Assert.Contains(list.EnumerateArray(), e => e.GetProperty("id").GetGuid() == task.Id);

        var d = await DataAsync(await api.Admin.GetAsync($"/api/tasks/{task.Id}/reconciliation"));
        Assert.True(d.GetProperty("canReconcile").GetBoolean());
        Assert.Equal(task.RcsRetryCount, d.GetProperty("rcsRetryCount").GetInt32());
        Assert.Equal(task.RcsRequestCode, d.GetProperty("task").GetProperty("rcsRequestCode").GetString());
        Assert.Equal("Failed", d.GetProperty("task").GetProperty("state").GetString());
        Assert.Contains(d.GetProperty("rcsEvents").EnumerateArray(), e => e.GetProperty("event").GetString() == "RCS_CANCEL_RETRY_EXHAUSTED");
        Assert.Contains(d.GetProperty("reservations").EnumerateArray(), e => e.GetProperty("kind").GetString() == "SourceRack" && e.GetProperty("locationCode").GetString() == "A1");
        Assert.Equal("Full", d.GetProperty("rack").GetProperty("status").GetString());
        Assert.Equal("A1", d.GetProperty("rack").GetProperty("currentLocationCode").GetString());
    }

    [Fact]
    public async Task Only_an_admin_can_view_or_reconcile()
    {
        await using var h = await ReadyAsync(fixture);
        var task = await ExhaustedCancelAsync(h);
        await using var f = new DbApiFactory(fixture);
        var api = await ApiAsync(f);

        Assert.Equal(HttpStatusCode.Forbidden, (await api.Operator.GetAsync("/api/tasks/reconciliation")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Operator.GetAsync($"/api/tasks/{task.Id}/reconciliation")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Operator.PostAsJsonAsync($"/api/tasks/{task.Id}/reconciliation-check", new { note = "checked on site" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Operator.PostAsJsonAsync($"/api/tasks/{task.Id}/reconcile", new { actualLocationCode = "A1", user = "op1", note = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().WithClientHeader().GetAsync("/api/tasks/reconciliation")).StatusCode);

        var after = await h.GetTaskAsync(task.Id);
        Assert.True(after.RequiresReconciliation);                                // nothing was changed by the refused calls
        Assert.Single(await h.ActiveReservationsAsync());
    }

    [Fact]
    public async Task An_inconclusive_check_is_recorded_and_audited_and_releases_nothing()
    {
        await using var h = await ReadyAsync(fixture);
        var task = await ExhaustedCancelAsync(h);
        await using var f = new DbApiFactory(fixture);
        var api = await ApiAsync(f);

        var r = await api.Admin.PostAsJsonAsync($"/api/tasks/{task.Id}/reconciliation-check", new { note = "Rack khong thay tai A1, chua tim thay o vi tri khac" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var after = await h.GetTaskAsync(task.Id);
        Assert.Equal(TaskState.Failed, after.State);
        Assert.True(after.RequiresReconciliation);
        Assert.False(after.ReconciliationCompleted);
        Assert.Contains(after.History, x => x.EventName == "RECONCILIATION_CHECK_UNRESOLVED" && x.Detail!.Contains("admin1") && x.Detail.Contains("Rack khong thay"));
        Assert.Single(await h.ActiveReservationsAsync());                         // reservation kept
        Assert.Single(await h.RacksAtAsync("A1"));                                // rack record untouched
        Assert.True(await h.Db(db => db.AuditLog.AnyAsync(a => a.Action == "TASK_RECONCILE_CHECK_UNRESOLVED" && a.Actor == "admin1")));

        // the note is mandatory
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Admin.PostAsJsonAsync($"/api/tasks/{task.Id}/reconciliation-check", new { note = "" })).StatusCode);
    }

    [Fact]
    public async Task A_verified_reconciliation_releases_the_reservation_only_when_the_admin_states_the_real_location()
    {
        await using var h = await ReadyAsync(fixture);
        var task = await ExhaustedCancelAsync(h);
        await using var f = new DbApiFactory(fixture);
        var api = await ApiAsync(f);

        var r = await api.Admin.PostAsJsonAsync($"/api/tasks/{task.Id}/reconcile", new { actualLocationCode = "A1", user = "admin1", note = "Rack nhin thay tai A1" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var after = await h.GetTaskAsync(task.Id);
        Assert.False(after.RequiresReconciliation);
        Assert.True(after.ReconciliationCompleted);
        Assert.Empty(await h.ActiveReservationsAsync());
        var rack = Assert.Single(await h.RacksAtAsync("A1"));
        Assert.Equal(RackStatus.Full, rack.Status);
        Assert.True(await h.Db(db => db.AuditLog.AnyAsync(a => a.Action == "TASK_RECONCILE" && a.Actor == "admin1")));
        Assert.True(await h.Db(db => db.InventoryTransactions.AnyAsync(x => x.TaskId == task.Id && x.EventType == InventoryEventType.Reconciliation)));
    }

    [Fact]
    public async Task A_task_that_does_not_need_reconciliation_cannot_be_checked_or_reconciled()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;                     // healthy accepted task
        await using var f = new DbApiFactory(fixture);
        var api = await ApiAsync(f);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.Admin.PostAsJsonAsync($"/api/tasks/{t.Id}/reconciliation-check", new { note = "check" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.Admin.PostAsJsonAsync($"/api/tasks/{t.Id}/reconcile", new { actualLocationCode = "A1", user = "admin1", note = "x" })).StatusCode);
        Assert.Single(await h.ActiveReservationsAsync());
    }
}
