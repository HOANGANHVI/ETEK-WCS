using Microsoft.Extensions.DependencyInjection;
using WCS.Domain.Common;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-040 HTTP surface: the new actions are Admin only, and one request can be followed from its key to legs and RCS commands.</summary>
[Collection("Database")]
public class Cr040ApiTests(DatabaseFixture fixture)
{
    private async Task<(Harness H, DbApiFactory F, HttpClient Admin, HttpClient Operator)> ArrangeAsync()
    {
        var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        var f = new DbApiFactory(fixture);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator));
            await db.SaveChangesAsync();
        }
        async Task<HttpClient> As(string user) { var c = f.CreateClient().WithClientHeader(); Assert.Equal(200, await c.LoginAsync(user)); return c; }
        return (h, f, await As("admin1"), await As("op1"));
    }

    [Fact]
    public async Task Rack_records_queue_scope_and_PLC_simulation_are_for_the_Admin_only()
    {
        var (h, f, admin, op) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var rack = new { locationCode = "F1", rackKind = "Full", externalRackId = "R-API", reason = "found" };
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, "/api/inventory/racks", rack)).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Delete, $"/api/inventory/racks/{Guid.NewGuid()}?reason=x")).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Put, $"/api/processes/{Guid.NewGuid()}/queue-scope", new { scope = "a", reason = "x" })).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, "/api/plc-simulation/activate", new { confirmed = true })).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Get, "/api/plc-simulation/stations")).Status);

        var created = await admin.SendJsonAsync(HttpMethod.Post, "/api/inventory/racks", rack);
        Assert.Equal(201, created.Status);
        var id = created.Root.GetProperty("data").GetProperty("id").GetString();
        var moved = await admin.SendJsonAsync(HttpMethod.Put, $"/api/inventory/racks/{id}", new { locationCode = "F2", reason = "moved by hand" });
        Assert.Equal(200, moved.Status);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Delete, $"/api/inventory/racks/{id}?reason=scrapped")).Status);
        var unconfirmed = await admin.SendJsonAsync(HttpMethod.Post, "/api/plc-simulation/activate", new { confirmed = false });
        Assert.Equal("SIMULATION_CONFIRMATION_REQUIRED", unconfirmed.Root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, "/api/plc-simulation/activate", new { confirmed = true })).Status);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, "/api/plc-simulation/deactivate")).Status);
    }

    [Fact]
    public async Task One_request_can_be_followed_from_its_key_to_its_legs_and_the_RCS_commands()
    {
        var (h, f, admin, _) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        await h.StockAsync("A1", RackKind.Full, 1);
        var created = await admin.SendJsonAsync(HttpMethod.Post, "/api/processes", new { code = "api-trace", name = "trace", requestType = "PickupRequest", rackKind = "Full", triggerLocationCodes = new[] { "A1" } });
        var process = created.Root.GetProperty("data").GetProperty("id").GetString();
        var version = created.Root.GetProperty("data").GetProperty("versions")[0].GetProperty("id").GetString();
        var definition = new
        {
            firstStepKey = "s1",
            steps = new object[]
            {
                new { stepKey = "s1", order = 1, type = "Pickup", sourceMode = "Request", destinationMode = "Candidates", resolution = "BeforePick", nextStepKey = "done", waitBehavior = "Wait", errorBehavior = "Fail", candidates = new[] { new { role = "Destination", priority = 1, order = 1, locationCode = "F1" } } },
                new { stepKey = "done", order = 2, type = "ProcessCompletion", candidates = Array.Empty<object>() },
            },
        };
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/process-versions/{version}/definition", definition)).Status);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/process-versions/{version}/validate")).Status);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/process-versions/{version}/publish")).Status);
        // production default: a new publication is queued - it needs a scope and Ready (A1 has no PLC port here, so the station flag is removed for this test)
        await h.Db(async db => { var a1 = db.Locations.Single(l => l.Code == "A1"); a1.Configure(a1.Name, a1.RackCapacity, a1.AllowedRackKind, true, false, h.Clock.UtcNow); await db.SaveChangesAsync(); return 0; });
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/processes/{process}/queue-scope", new { scope = "trace", reason = "test" })).Status);
        var ready = await admin.SendJsonAsync(HttpMethod.Put, $"/api/process-versions/{version}/readiness", new { readiness = "Ready", reason = "test" });
        Assert.True(ready.Status == 200, ready.Root.ToString());

        var start = await admin.SendJsonAsync(HttpMethod.Post, "/api/routing/orders", new { requestKey = "TRACE-1", locationCode = "A1", requestType = "PickupRequest", rackKind = "Full", processCode = "API-TRACE" });
        Assert.True(start.Status == 201, start.Root.ToString());
        var id = start.Root.GetProperty("data").GetProperty("order").GetProperty("id").GetString();
        var detail = (await admin.SendJsonAsync(HttpMethod.Get, $"/api/routing/orders/{id}")).Root.GetProperty("data");
        var order = detail.GetProperty("order");
        Assert.Equal("TRACE-1", order.GetProperty("requestKey").GetString());
        Assert.Equal("TRACE", order.GetProperty("queueScope").GetString());
        var leg = order.GetProperty("legs").EnumerateArray().Single();
        var code = leg.GetProperty("agvRequestCode").GetString();
        var commands = detail.GetProperty("commands").EnumerateArray().ToList();
        Assert.NotEmpty(commands);
        Assert.All(commands, c => Assert.Equal(code, c.GetProperty("requestCode").GetString()));
    }

    [Fact]
    public async Task The_CR041_actions_are_Admin_only_and_the_simulation_history_keeps_who_set_which_value()
    {
        var (h, f, admin, op) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var id = Guid.NewGuid();
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Get, $"/api/inventory/racks/{id}/editability")).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, $"/api/processes/{id}/apply-catalog", new { reason = "x" })).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Get, "/api/plc-simulation/history")).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, "/api/plc-simulation/stations/A1/reset")).Status);
        Assert.Equal(404, (await admin.SendJsonAsync(HttpMethod.Get, $"/api/inventory/racks/{id}/editability")).Status);
        await admin.SendJsonAsync(HttpMethod.Put, "/api/plc-simulation/stations/A1/signals/Pickup_Request", new { value = "on" });   // refused (not active): still on record
        var history = (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc-simulation/history")).Root.GetProperty("data").ToString();
        Assert.Contains("PLC_SIM_SIGNAL", history); Assert.Contains("A1.Pickup_Request", history); Assert.Contains("admin1", history);
    }
}
