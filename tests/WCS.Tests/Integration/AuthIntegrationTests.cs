using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WCS.Application.Auth;
using WCS.Domain.Common;
using WCS.Domain.Tasks;
using WCS.Domain.Users;
using WCS.Infrastructure.Auth;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>The real API against a real (throw-away) PostgreSQL database. Hosted workers are removed so tests control every state change.</summary>
public sealed class DbApiFactory(DatabaseFixture db) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WcsDatabase"] = db.ConnectionString,
            ["Database:Password"] = null,
        }));
        builder.ConfigureServices(s => s.RemoveAll<IHostedService>());
    }
}

[Collection("Database")]
public class AuthIntegrationTests(DatabaseFixture fixture)
{
    private const string Pw = TestUsers.Password;

    private static async Task AddUsersAsync(DbApiFactory f, params (string Name, UserRole Role)[] users)
    {
        using var scope = f.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        foreach (var (name, role) in users) ctx.Users.Add(TestUsers.Make(name, role));
        await ctx.SaveChangesAsync();
    }

    private static async Task<HttpClient> As(DbApiFactory f, string user)
    {
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync(user));
        return c;
    }

    private async Task<(Harness H, DbApiFactory F)> ArrangeAsync(int a1Racks = 1)
    {
        var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 5, stationCapacity: 3);
        await h.StockAsync("A1", RackKind.Full, a1Racks);
        await h.StockAsync("A2", RackKind.Full, 1);
        var f = new DbApiFactory(fixture);
        await AddUsersAsync(f, ("admin1", UserRole.Admin), ("op1", UserRole.Operator), ("op2", UserRole.Operator));
        return (h, f);
    }

    /// <summary>Drives the simulated devices of the API host itself (its RCS simulator is a different instance from the harness one).</summary>
    private static async Task AdvanceUntil(DbApiFactory f, Harness h, Guid id, TaskState state)
    {
        for (var i = 0; i < 40; i++)
        {
            if ((await h.GetTaskAsync(id)).State == state) return;
            using var scope = f.Services.CreateScope();
            var step = await scope.ServiceProvider.GetRequiredService<WCS.Infrastructure.Simulation.SimulationDriver>().AdvanceAsync(id);
            if (step.Signal is null) await scope.ServiceProvider.GetRequiredService<WCS.Application.Abstractions.ITaskScheduler>().RunOnceAsync();
        }
        Assert.Fail($"Task did not reach {state}; stuck in {(await h.GetTaskAsync(id)).State}.");
    }

    private static async Task<Guid> CreateTaskAs(HttpClient c, string key, string flow = "BP01", string station = "A1")
    {
        var r = await c.SendJsonAsync(HttpMethod.Post, "/api/tasks", new { requestKey = key, flow, stationCode = station, requestedBy = "forged-name" });
        Assert.True(r.Status is 200 or 201, r.Raw);
        return r.Root.GetProperty("data").GetProperty("id").GetGuid();
    }

    // ---------------------------------------------------------------- task permissions
    [Fact]
    public async Task Operator_can_create_a_task_and_the_requester_comes_from_the_session()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");

        var id = await CreateTaskAs(op, "k1");
        var task = await h.GetTaskAsync(id);
        Assert.Equal("op1", task.RequestedBy);                    // the body said "forged-name"
        Assert.Equal(TaskState.Accepted, task.State);             // AGV1 was free, so it was dispatched and accepted (simulated RCS)
    }

    [Fact]
    public async Task Operator_can_delete_a_waiting_task_and_a_refused_edit_leaves_it_untouched()
    {
        var (h, f) = await ArrangeAsync(a1Racks: 1);
        await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");

        var busy = await CreateTaskAs(op, "busy");                         // takes AGV1 and A1's only rack
        var queued = await CreateTaskAs(op, "queued", station: "A2");      // waits for AGV1
        Assert.Equal(TaskState.Queued, (await h.GetTaskAsync(queued)).State);

        var edit = await op.SendJsonAsync(HttpMethod.Put, $"/api/tasks/{queued}", new { stationCode = "A1", expectedState = "Queued" });
        Assert.Equal(422, edit.Status);                                     // A1 has nothing left to pick
        Assert.Equal("NO_SOURCE_AT_STATION", edit.Root.ErrorCode());
        var untouched = await h.GetTaskAsync(queued);
        Assert.Equal((await h.Db(d => d.Locations.AsNoTracking().SingleAsync(l => l.Code == "A2"))).Id, untouched.RequestLocationId);
        Assert.Equal(TaskState.Queued, untouched.State);
        Assert.Equal(2, (await h.ActiveReservationsAsync()).Count);        // its reservation is still held

        var del = await op.SendJsonAsync(HttpMethod.Delete, $"/api/tasks/{queued}?expectedState=Queued");
        Assert.Equal(200, del.Status);
        var deleted = await h.GetTaskAsync(queued);
        Assert.Equal(TaskState.Cancelled, deleted.State);                   // the record and its history are kept
        Assert.Contains(deleted.History, e => e.EventName == "TASK_DELETED");
        Assert.Single(await h.ActiveReservationsAsync());                   // only the running task keeps its rack
        Assert.NotEqual(TaskState.Cancelled, (await h.GetTaskAsync(busy)).State);
    }

    [Fact]
    public async Task Operator_can_move_a_waiting_task_to_another_allowed_station()
    {
        var (h, f) = await ArrangeAsync(a1Racks: 2);
        await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        await CreateTaskAs(op, "busy");                                     // A1 rack #1
        var queued = await CreateTaskAs(op, "queued", station: "A2");       // reserves A2's rack

        var bad = await op.SendJsonAsync(HttpMethod.Put, $"/api/tasks/{queued}", new { stationCode = "B1" });
        Assert.Equal(422, bad.Status); Assert.Equal("STATION_NOT_ALLOWED", bad.Root.ErrorCode());   // BP01 requests come from A1/A2 only

        var ok = await op.SendJsonAsync(HttpMethod.Put, $"/api/tasks/{queued}", new { stationCode = "A1", expectedState = "Queued" });
        Assert.Equal(200, ok.Status);
        var a1 = await h.LocationAsync("A1");
        var moved = await h.GetTaskAsync(queued);
        Assert.Equal(a1.Id, moved.RequestLocationId);
        Assert.Equal(a1.Id, moved.SourceLocationId);                         // source re-selected for the new station
        Assert.Contains(moved.History, e => e.EventName == "TASK_EDITED");
        Assert.Equal(2, (await h.ActiveReservationsAsync()).Count);          // old reservation released, new one taken
        Assert.Equal(2, (await h.ActiveReservationsAsync()).Select(r => r.RackId).Distinct().Count());

        var same = await op.SendJsonAsync(HttpMethod.Put, $"/api/tasks/{queued}", new { stationCode = "A1" });
        Assert.Equal(200, same.Status);                                     // no-op
    }

    [Fact]
    public async Task Operator_cannot_edit_or_delete_a_task_after_the_AGV_has_accepted_it()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        var id = await CreateTaskAs(op, "taken");
        await AdvanceUntil(f, h, id, TaskState.Accepted);

        var edit = await op.SendJsonAsync(HttpMethod.Put, $"/api/tasks/{id}", new { stationCode = "A2" });
        var del = await op.SendJsonAsync(HttpMethod.Delete, $"/api/tasks/{id}");
        Assert.Equal(403, edit.Status); Assert.Equal("TASK_LOCKED", edit.Root.ErrorCode());
        Assert.Equal(403, del.Status);
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(id)).State);

        // executing and completed tasks are locked as well
        await AdvanceUntil(f, h, id, TaskState.Executing);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Delete, $"/api/tasks/{id}")).Status);
        await AdvanceUntil(f, h, id, TaskState.Completed);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Put, $"/api/tasks/{id}", new { stationCode = "A2" })).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Delete, $"/api/tasks/{id}")).Status);
    }

    [Fact]
    public async Task A_forged_expected_state_cannot_get_a_locked_task_changed()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        var id = await CreateTaskAs(op, "forge");
        await AdvanceUntil(f, h, id, TaskState.Accepted);

        var forged = await op.SendJsonAsync(HttpMethod.Delete, $"/api/tasks/{id}?expectedState=Queued");
        Assert.Equal(409, forged.Status);                               // the screen was stale: reload
        Assert.Equal("TASK_STATE_CHANGED", forged.Root.ErrorCode());
        var noHint = await op.SendJsonAsync(HttpMethod.Delete, $"/api/tasks/{id}");
        Assert.Equal(403, noHint.Status);                                // and without the hint the database state still decides
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(id)).State);
    }

    [Fact]
    public async Task Admin_can_edit_and_delete_waiting_tasks_but_gets_a_business_error_for_running_ones()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        var first = await CreateTaskAs(admin, "a1");
        var waiting = await CreateTaskAs(admin, "a2", station: "A2");

        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Delete, $"/api/tasks/{waiting}")).Status);
        Assert.Equal(TaskState.Cancelled, (await h.GetTaskAsync(waiting)).State);

        await AdvanceUntil(f, h, first, TaskState.Executing);
        var r = await admin.SendJsonAsync(HttpMethod.Delete, $"/api/tasks/{first}");
        Assert.Equal(409, r.Status); Assert.Equal("TASK_NOT_EDITABLE", r.Root.ErrorCode());
        // Admin handles in-flight tasks through the existing cancel action instead
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/tasks/{first}/cancel", new { reason = "test" })).Status);
    }

    [Fact]
    public async Task Cancel_and_stock_changes_are_admin_only()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        var id = await CreateTaskAs(op, "x");
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, $"/api/tasks/{id}/cancel", new { reason = "no" })).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, "/api/inventory/initial-stock", new { locationCode = "A1", rackKind = "Full", quantity = 1, user = "x" })).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, $"/api/simulator/tasks/{id}/advance")).Status);
        Assert.NotEqual(TaskState.Cancelled, (await h.GetTaskAsync(id)).State);
        // read access for both roles
        foreach (var url in new[] { "/api/tasks", "/api/agvs", "/api/locations", "/api/inventory/racks", "/api/inventory/transactions", "/api/system/health" })
            Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Get, url)).Status);
    }

    [Fact]
    public async Task Simulator_works_for_admin_and_is_audited()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        var id = await CreateTaskAs(admin, "sim");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/simulator/tasks/{id}/advance")).Status);
        var audit = await admin.SendJsonAsync(HttpMethod.Get, "/api/audit?action=SIMULATOR_ADVANCE");
        Assert.Equal(1, audit.Root.GetProperty("data").GetArrayLength());
    }

    // ---------------------------------------------------------------- race conditions
    [Fact]
    public async Task Deleting_a_waiting_task_while_the_AGV_frees_up_never_deletes_a_task_that_got_dispatched()
    {
        await using var f = new DbApiFactory(fixture);
        for (var round = 0; round < 6; round++)
        {
            await using var h = await Harness.CreateAsync(fixture);
            await h.SeedStandardAsync(bufferCapacity: 5, stationCapacity: 3);
            await h.StockAsync("A1", RackKind.Full, 1);
            await h.StockAsync("A2", RackKind.Full, 1);
            await AddUsersAsync(f, ("op1", UserRole.Operator));
            var op = await As(f, "op1");
            var busy = await CreateTaskAs(op, "busy");
            var q = await CreateTaskAs(op, "q", station: "A2");
            Assert.Equal(TaskState.Queued, (await h.GetTaskAsync(q)).State);

            // The first task completes (which dispatches the queued one) at the same moment the operator deletes the queued one.
            var finish = Task.Run(() => AdvanceUntil(f, h, busy, TaskState.Completed));
            var delete = op.SendJsonAsync(HttpMethod.Delete, $"/api/tasks/{q}");
            await Task.WhenAll(finish, delete);

            var after = await h.GetTaskAsync(q);
            var status = (await delete).Status;
            if (after.State == TaskState.Cancelled)
            {
                Assert.Equal(200, status);
                Assert.Null(after.RcsTaskCode);                              // it never reached RCS
                Assert.Empty(await h.ActiveReservationsAsync());
            }
            else Assert.True(status is 403 or 409, $"round {round}: state {after.State}, delete returned {status}");
            Assert.True(await h.ActiveTaskCountAsync("AGV1") <= 1);
        }
    }

    // ---------------------------------------------------------------- accounts on the real database
    [Fact]
    public async Task Two_admins_demoting_each_other_at_the_same_time_cannot_leave_zero_admins()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        await AddUsersAsync(f, ("admin2", UserRole.Admin));
        using var s1 = f.Services.CreateScope(); using var s2 = f.Services.CreateScope();
        var a1 = await s1.ServiceProvider.GetRequiredService<IUserRepository>().GetByUsernameAsync("admin1");
        var a2 = await s1.ServiceProvider.GetRequiredService<IUserRepository>().GetByUsernameAsync("admin2");

        async Task<string> Demote(IServiceScope scope, AppUser actor, AppUser target)
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<UserAdminService>().UpdateAsync(new ActorInfo(actor.Id, actor.Username, UserRole.Admin),
                    target.Id, new UpdateUserCommand(target.DisplayName, UserRole.Operator, true));
                return "ok";
            }
            catch (WCS.Application.Common.AppException e) { return e.Code; }
        }

        for (var i = 0; i < 5; i++)
        {
            var results = await Task.WhenAll(Demote(s1, a1!, a2!), Demote(s2, a2!, a1!));
            using var check = f.Services.CreateScope();
            var admins = await check.ServiceProvider.GetRequiredService<IUserRepository>().ListActiveAdminsAsync();
            Assert.True(admins.Count >= 1, $"iteration {i}: results {string.Join(",", results)} left no admin");
            if (admins.Count == 1) break;     // one demotion succeeded; the other was refused as LAST_ADMIN
        }
    }

    [Fact]
    public async Task Passwords_are_stored_hashed_and_the_audit_trail_survives_in_the_database()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        await admin.SendJsonAsync(HttpMethod.Post, "/api/users", new { username = "dbop", displayName = "DB Op", role = "Operator", password = "Stored-Hash-88" });
        await admin.SendJsonAsync(HttpMethod.Put, "/api/config/plc", new { enabled = false, deviceName = "PLC-DB", ipAddress = "192.0.2.50", port = 5007, protocol = "McProtocol3EBinaryTcp", timeoutMs = 3000 });

        var raw = await h.Db(async db => new
        {
            Hashes = await db.Users.AsNoTracking().Select(u => u.PasswordHash).ToListAsync(),
            Audit = await db.AuditLog.AsNoTracking().ToListAsync(),
        });
        Assert.All(raw.Hashes, hash => { Assert.DoesNotContain("Stored-Hash-88", hash); Assert.DoesNotContain(Pw, hash); });
        Assert.Contains(raw.Audit, a => a is { Action: "AUTH_LOGIN", Result: AuditResult.Success, Actor: "admin1" });
        var create = raw.Audit.Single(a => a.Action == "USER_CREATE");
        Assert.DoesNotContain("Stored-Hash-88", create.AfterJson ?? "");
        var plc = raw.Audit.Single(a => a.Action == "CONFIG_PLC_UPDATE");
        Assert.NotNull(plc.BeforeJson); Assert.Contains("192.0.2.50", plc.AfterJson);
        Assert.All(raw.Audit, a => Assert.DoesNotContain("Stored-Hash-88", a.Detail + a.BeforeJson + a.AfterJson));
    }

    // ---------------------------------------------------------------- first administrator
    [Fact]
    public async Task Bootstrap_creates_the_first_admin_once_and_only_from_deployment_configuration()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await using var f = new DbApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Bootstrap:Admin:Username"] = "FirstAdmin", ["Bootstrap:Admin:Password"] = "Boot-Strap-99" })));
        using (var scope = f.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().RunAsync();
        using (var scope = f.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().RunAsync();   // second run: no-op

        var users = await h.Db(db => db.Users.AsNoTracking().ToListAsync());
        var u = Assert.Single(users);
        Assert.Equal("firstadmin", u.Username);
        Assert.Equal(UserRole.Admin, u.Role);
        Assert.True(u.MustChangePassword);
        Assert.DoesNotContain("Boot-Strap-99", u.PasswordHash);
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync("firstadmin", "Boot-Strap-99"));
        Assert.Equal(403, (await c.SendJsonAsync(HttpMethod.Get, "/api/config/plc")).Status);   // must change the password first
    }

    [Fact]
    public async Task Default_admin_account_is_created_once_forces_a_password_change_and_is_never_recreated_or_reset()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await using var f = new DbApiFactory(fixture);
        async Task RunBootstrap() { using var scope = f.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().RunAsync(); }
        await RunBootstrap(); await RunBootstrap();

        var u = Assert.Single(await h.Db(db => db.Users.AsNoTracking().ToListAsync()));
        Assert.Equal("admin", u.Username); Assert.Equal(UserRole.Admin, u.Role); Assert.True(u.MustChangePassword);
        Assert.StartsWith("AQAAAA", u.PasswordHash);                       // Identity v3 hash, not the literal password

        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync("admin", "admin"));
        var blocked = await c.SendJsonAsync(HttpMethod.Get, "/api/agvs");
        Assert.Equal(403, blocked.Status); Assert.Equal("PASSWORD_CHANGE_REQUIRED", blocked.Root.ErrorCode());   // the change cannot be skipped
        Assert.Equal(400, (await c.SendJsonAsync(HttpMethod.Post, "/api/auth/change-password", new { currentPassword = "admin", newPassword = "admin" })).Status);
        Assert.Equal(200, (await c.SendJsonAsync(HttpMethod.Post, "/api/auth/change-password", new { currentPassword = "admin", newPassword = "Changed-Root-2026" })).Status);
        Assert.Equal(200, (await c.SendJsonAsync(HttpMethod.Get, "/api/agvs")).Status);

        // a restart must neither recreate nor reset anything, and admin/admin no longer works
        await RunBootstrap();
        Assert.Equal(401, await f.CreateClient().WithClientHeader().LoginAsync("admin", "admin"));
        Assert.Equal(200, await f.CreateClient().WithClientHeader().LoginAsync("admin", "Changed-Root-2026"));
        Assert.Single(await h.Db(db => db.Users.AsNoTracking().ToListAsync()));
    }

    [Fact]
    public async Task Default_admin_is_not_created_in_Production_unless_a_bootstrap_password_is_configured()
    {
        await using var h = await Harness.CreateAsync(fixture);
        async Task<int> Run(Dictionary<string, string?> cfg)
        {
            await using var f = new DbApiFactory(fixture).WithWebHostBuilder(b => { b.UseEnvironment("Production"); b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(cfg)); });
            using var scope = f.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().RunAsync();
            return await h.Db(db => db.Users.CountAsync());
        }
        Assert.Equal(0, await Run(new()));                                                          // no admin/admin on a production system
        Assert.Equal(1, await Run(new() { ["Bootstrap:Admin:Password"] = "Prod-Bootstrap-77" }));  // explicit, separate password
    }

    [Fact]
    public async Task Bootstrap_never_touches_an_existing_account_named_admin()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await using var f = new DbApiFactory(fixture);
        await AddUsersAsync(f, ("admin", UserRole.Operator));      // same name, not an admin, password = TestUsers.Password
        using (var scope = f.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().RunAsync();
        var u = Assert.Single(await h.Db(db => db.Users.AsNoTracking().ToListAsync()));
        Assert.Equal(UserRole.Operator, u.Role);
        Assert.Equal(200, await f.CreateClient().WithClientHeader().LoginAsync("admin", TestUsers.Password));   // password untouched
    }

    [Fact]
    public async Task Bootstrap_does_nothing_without_configuration_or_with_a_weak_password_or_when_an_admin_exists()
    {
        await using var h = await Harness.CreateAsync(fixture);
        async Task<int> Run(Dictionary<string, string?> cfg)
        {
            await using var f = new DbApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(cfg)));
            using var scope = f.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().RunAsync();
            return await h.Db(db => db.Users.CountAsync());
        }
        Assert.Equal(0, await Run(new() { ["Bootstrap:Admin:AllowDefaultCredentials"] = "false" }));                                // default account switched off
        Assert.Equal(0, await Run(new() { ["Bootstrap:Admin:Username"] = "boss", ["Bootstrap:Admin:Password"] = "weak" }));        // policy
        Assert.Equal(1, await Run(new() { ["Bootstrap:Admin:Username"] = "boss", ["Bootstrap:Admin:Password"] = "Strong-Pass-123" }));
        Assert.Equal(1, await Run(new() { ["Bootstrap:Admin:Username"] = "other", ["Bootstrap:Admin:Password"] = "Strong-Pass-123" })); // admin exists
    }
}
