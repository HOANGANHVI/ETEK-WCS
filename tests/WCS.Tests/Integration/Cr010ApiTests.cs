using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-010 HTTP surface: authorization (Admin configures, Operator only watches), process administration flow and audit.</summary>
[Collection("Database")]
public class Cr010ApiTests(DatabaseFixture fixture)
{
    private async Task<(Harness H, DbApiFactory F)> ArrangeAsync()
    {
        var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        var f = new DbApiFactory(fixture);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator));
        await db.SaveChangesAsync();
        return (h, f);
    }

    private static async Task<HttpClient> As(DbApiFactory f, string user)
    {
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync(user));
        return c;
    }

    [Fact]
    public async Task Operator_can_watch_Transport_Tasks_and_Station_Transactions_but_cannot_configure_anything()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");

        Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Get, "/api/routing/orders")).Status);
        Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Get, "/api/routing/transactions?activeOnly=true")).Status);

        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Get, "/api/processes")).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, "/api/processes/import-reference")).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, "/api/plc/connections", new { plcId = "PLC-X", name = "x", ipAddress = "1.1.1.1" })).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, $"/api/routing/legs/{Guid.NewGuid()}/pick")).Status);   // physical-event inputs are Admin only
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, "/api/routing/plc-requests", new { plcId = "PLC-X", requestId = "r" })).Status);
    }

    [Fact]
    public async Task Admin_imports_reference_processes_validates_and_is_refused_for_BP04_with_the_reason_and_everything_is_audited()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");

        // CR-039: what the host start-up does (this test host runs no start-up seed)
        await h.Scoped(sp => sp.GetRequiredService<WCS.Infrastructure.Seeding.WcsDataSeeder>().SeedDefaultLocationsAsync());
        await h.Scoped(sp => sp.GetRequiredService<WCS.Infrastructure.Seeding.DefaultProcessSeeder>().SeedAsync());

        var imp = await admin.SendJsonAsync(HttpMethod.Post, "/api/processes/import-reference");
        Assert.Equal(200, imp.Status);
        // the 12 Phase 2 defaults exist, so the import adds only the six Phase 1 reference drafts
        Assert.Equal((6, 12), (imp.Root.GetProperty("data").GetProperty("imported").GetInt32(), imp.Root.GetProperty("data").GetProperty("skipped").GetInt32()));

        var startable = (await admin.SendJsonAsync(HttpMethod.Get, "/api/routing/startable-processes")).Root.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(["PH2-01", "PH2-02", "PH2-03", "PH2-04", "PH2-06", "PH2-07", "PH2-08", "PH2-09", "PH2-10", "PH2-11", "PH2-12"], startable.Select(p => p.GetProperty("code").GetString()).ToArray());
        Assert.All(startable, p => { Assert.False(p.GetProperty("acceptsRequests").GetBoolean()); Assert.Equal("PROCESS_NOT_READY", p.GetProperty("blockedCode").GetString()); });
        var refused = await admin.SendJsonAsync(HttpMethod.Post, "/api/routing/orders", new { requestKey = "api-ph2", locationCode = "X1", requestType = "PickupRequest", rackKind = "Full", processCode = "PH2-07" });
        Assert.Equal("PROCESS_NOT_READY", refused.Root.GetProperty("error").GetProperty("code").GetString());

        var list = await admin.SendJsonAsync(HttpMethod.Get, "/api/processes");
        var bp4 = list.Root.GetProperty("data").EnumerateArray().Single(p => p.GetProperty("code").GetString() == "BP-04");
        Assert.True(bp4.GetProperty("reviewBlocked").GetBoolean());
        var versionId = bp4.GetProperty("versions")[0].GetProperty("id").GetString();

        var val = await admin.SendJsonAsync(HttpMethod.Post, $"/api/process-versions/{versionId}/validate");
        Assert.Equal(200, val.Status);
        Assert.Equal("Draft", val.Root.GetProperty("data").GetProperty("state").GetString());
        Assert.Contains(val.Root.GetProperty("data").GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "PROCESS_REVIEW_BLOCKED");

        var pub = await admin.SendJsonAsync(HttpMethod.Post, $"/api/process-versions/{versionId}/publish");
        Assert.Equal(422, pub.Status);                                                             // never published

        var audit = await admin.SendJsonAsync(HttpMethod.Get, "/api/processes/audit");
        var actions = audit.Root.GetProperty("data").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Contains("PROCESS_IMPORT", actions);
        Assert.Contains("PROCESS_VALIDATE", actions);
        Assert.Contains("PROCESS_PUBLISH", actions);                                               // the refused attempt is audited too (Failed)
    }

    [Fact]
    public async Task Admin_builds_a_process_over_HTTP_without_any_code_change_and_adds_a_PLC()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");

        var created = await admin.SendJsonAsync(HttpMethod.Post, "/api/processes", new { code = "api-1", name = "API process", requestType = "PickupRequest", rackKind = "Full", triggerLocationCodes = new[] { "A1" } });
        Assert.Equal(201, created.Status);
        var versionId = created.Root.GetProperty("data").GetProperty("versions")[0].GetProperty("id").GetString();
        Assert.Equal("API-1", created.Root.GetProperty("data").GetProperty("code").GetString());

        var def = new
        {
            firstStepKey = "s1",
            steps = new object[]
            {
                new { stepKey = "s1", order = 1, type = "Delivery", sourceMode = "Request", destinationMode = "Candidates", resolution = "BeforePick", nextStepKey = "done", waitBehavior = "Wait", errorBehavior = "Fail",
                      candidates = new[] { new { role = "Destination", priority = 1, order = 1, locationCode = "B1", noBind = false } } },
                new { stepKey = "done", order = 2, type = "ProcessCompletion" },
            },
        };
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/process-versions/{versionId}/definition", def)).Status);
        var v = await admin.SendJsonAsync(HttpMethod.Post, $"/api/process-versions/{versionId}/validate");
        // no PLC port exists in this database, so the route has no approval problem: it validates and can be published
        Assert.Equal("Validated", v.Root.GetProperty("data").GetProperty("state").GetString());
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/process-versions/{versionId}/publish")).Status);

        // an unknown condition type cannot be stored
        var bad = new { firstStepKey = "c", steps = new object[] { new { stepKey = "c", order = 1, type = "Condition", nextStepKey = "d", alternativeNextStepKey = "d", conditions = new[] { new { type = "Script", parameter = "1==1", locationCode = "B1" } } }, new { stepKey = "d", order = 2, type = "ProcessCompletion" } } };
        var newVer = await admin.SendJsonAsync(HttpMethod.Post, $"/api/processes/{created.Root.GetProperty("data").GetProperty("id").GetString()}/versions", new { });
        Assert.Equal(201, newVer.Status);
        var rejected = await admin.SendJsonAsync(HttpMethod.Put, $"/api/process-versions/{newVer.Root.GetProperty("data").GetProperty("id").GetString()}/definition", bad);
        Assert.Equal(400, rejected.Status);

        var plc = await admin.SendJsonAsync(HttpMethod.Post, "/api/plc/connections", new { plcId = "PLC-NEW", name = "New PLC", ipAddress = "10.1.1.1", stations = new[] { new { stationId = "NEW-1" }, new { stationId = "NEW-2" } } });
        Assert.Equal(201, plc.Status);
        Assert.False(plc.Root.GetProperty("data").GetProperty("enabled").GetBoolean());
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        Assert.Equal(2, await db.PlcStations.CountAsync(s => s.PlcId == "PLC-NEW"));
        Assert.True(await db.StationIoPoints.CountAsync(p => p.PlcId == "PLC-NEW") > 0);
    }
}
