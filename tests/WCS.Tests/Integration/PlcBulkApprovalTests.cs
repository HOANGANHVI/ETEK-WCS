using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>Bulk READ approval of the commissioning PLC (PLC-04): eligibility, the existing approval path, isolation from the other PLCs, audit, verification and "never a write".</summary>
[Collection("Database")]
public class PlcBulkApprovalTests(DatabaseFixture fixture)
{
    private async Task<(Harness H, WebApplicationFactory<Program> F)> ArrangeAsync(Dictionary<string, string?>? config = null)
    {
        var h = await Harness.CreateAsync(fixture);
        var f0 = new DbApiFactory(fixture);
        using (var scope = f0.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator));
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>().SeedAsync();
        }
        await f0.DisposeAsync();
        WebApplicationFactory<Program> f = new DbApiFactory(fixture);
        if (config is not null) f = f.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(config)));
        return (h, f);
    }

    private static async Task<HttpClient> As(WebApplicationFactory<Program> f, string user)
    {
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync(user));
        return c;
    }

    private static string Url(string plc) => $"/api/plc/connections/{plc}/bulk-approval";

    /// <summary>Enters an address through the normal API for the first <paramref name="count"/> points of a station (valid ranges, never overlapping).</summary>
    private static async Task<List<(Guid Id, string Signal, string Address)>> EnterAddressesAsync(Harness h, HttpClient admin, string plc, string station, int count, int start)
    {
        var pts = await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.PlcId == plc && p.StationId == station).OrderBy(p => p.Seq).Take(count).ToListAsync());
        var done = new List<(Guid, string, string)>();
        var n = start;
        foreach (var p in pts)
        {
            var (addr, body) = p.DataType switch
            {
                PlcDataType.Bool => ($"M{9000 + n}", (object)new { address = $"M{9000 + n}" }),
                PlcDataType.String => ($"D{3000 + n * 20}", new { address = $"D{3000 + n * 20}", wordLength = 10, encoding = "ASCII" }),
                _ => ($"D{2000 + n * 2}", new { address = $"D{2000 + n * 2}" }),
            };
            Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{p.Id}/address", body)).Status);
            done.Add((p.Id, p.SignalName, addr)); n++;
        }
        return done;
    }

    private static Task<List<StationIoPoint>> Points(Harness h, string plc) => h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.PlcId == plc).ToListAsync());

    [Fact]
    public async Task Admin_previews_then_approves_only_the_eligible_points_of_PLC_04_and_they_become_pollable()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        var entered = await EnterAddressesAsync(h, admin, "PLC-04", "E1", 10, 0);                 // 10 valid addresses
        await EnterAddressesAsync(h, admin, "PLC-01", "A1", 3, 100);                              // another PLC: must stay untouched
        // one PLC-04 point carries an invalid combination (BOOL on a word device): entered directly, as legacy data could be
        var bad = await h.Db(async db =>
        {
            var p = await db.StationIoPoints.Where(x => x.PlcId == "PLC-04" && x.StationId == "E2" && x.DataType == PlcDataType.Bool).OrderBy(x => x.Seq).FirstAsync();
            p.ConfigureAddress("D7000", null, null, DateTimeOffset.UtcNow); await db.SaveChangesAsync(); return p.Id;
        });
        var preApproved = entered[0].Id;
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{preApproved}/approve")).Status);     // approved earlier, by the existing step
        var approvedAt = await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.Id == preApproved).Select(p => p.ApprovedAt).SingleAsync());

        var preview = await admin.SendJsonAsync(HttpMethod.Get, Url("PLC-04"));
        Assert.Equal(200, preview.Status);
        var pv = preview.Root.GetProperty("data");
        Assert.Equal(92, pv.GetProperty("total").GetInt32());
        Assert.Equal(11, pv.GetProperty("withAddress").GetInt32());                               // 10 entered + the invalid one
        Assert.Equal(9, pv.GetProperty("eligible").GetInt32());                                   // the pre-approved one is not counted again
        Assert.Equal(1, pv.GetProperty("alreadyApproved").GetInt32());
        Assert.Equal(82, pv.GetProperty("notEligible").GetInt32());                               // 81 without address + 1 invalid
        Assert.Equal(1, await h.Db(db => db.StationIoPoints.CountAsync(p => p.PlcId == "PLC-04" && p.Status == IoConfigStatus.Approved)));   // preview changed nothing

        var run = await admin.SendJsonAsync(HttpMethod.Post, Url("PLC-04"), new { confirm = true, expectedEligible = 9 });
        Assert.Equal(200, run.Status);
        var r = run.Root.GetProperty("data");
        Assert.Equal((9, 1, 82, 0, 10), (r.GetProperty("approved").GetInt32(), r.GetProperty("alreadyApproved").GetInt32(), r.GetProperty("notEligible").GetInt32(),
            r.GetProperty("failed").GetInt32(), r.GetProperty("verifiedPollable").GetInt32()));

        var after = await Points(h, "PLC-04");
        Assert.Equal(10, after.Count(p => p.Status == IoConfigStatus.Approved));
        Assert.All(after.Where(p => p.Status == IoConfigStatus.Approved), p => { Assert.True(p.IsPollable); Assert.Equal("admin1", p.ApprovedBy); Assert.False(p.WriteAllowed); });
        Assert.Equal(approvedAt, after.Single(p => p.Id == preApproved).ApprovedAt);              // an approved point is not processed again
        Assert.Equal(IoConfigStatus.Configured, after.Single(p => p.Id == bad).Status);           // invalid: left as it was, reported
        Assert.Equal("D7000", after.Single(p => p.Id == bad).ActualAddress);
        Assert.Contains(r.GetProperty("items").EnumerateArray(), i => i.GetProperty("pointId").GetGuid() == bad && i.GetProperty("outcome").GetString() == "NOT_ELIGIBLE" && i.GetProperty("code").GetString() == "TYPE_DEVICE");
        Assert.All(after.Where(p => p.Status == IoConfigStatus.NeedsConfiguration), p => Assert.Null(p.ActualAddress));   // no address: not approved
        Assert.All(entered, e => Assert.Equal(e.Address, after.Single(p => p.Id == e.Id).ActualAddress));                   // addresses unchanged

        var other = await Points(h, "PLC-01");                                                    // another PLC untouched
        Assert.Equal(0, other.Count(p => p.Status == IoConfigStatus.Approved));
        Assert.Equal(3, other.Count(p => p.Status == IoConfigStatus.Configured));
        var api = await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/points?plcId=PLC-04");      // the API agrees with the database
        Assert.Equal(10, api.Root.GetProperty("data").EnumerateArray().Count(p => p.GetProperty("pollable").GetBoolean()));
    }

    [Fact]
    public async Task The_operation_is_audited_and_changes_neither_the_mode_nor_any_write_permission_nor_sends_anything_to_a_PLC()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        await EnterAddressesAsync(h, admin, "PLC-04", "E1", 4, 0);
        var connBefore = await h.Db(db => db.PlcConnections.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.Enabled, c.IpAddress, c.Port, c.Protocol }).ToListAsync());
        var modeBefore = (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Root.GetProperty("data").GetProperty("runtime").GetProperty("effectiveMode").GetString();

        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, Url("PLC-04"), new { confirm = true, expectedEligible = 4 })).Status);

        var audit = await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action == "PLC_IO_BULK_APPROVE").ToListAsync());
        var row = Assert.Single(audit);
        Assert.Equal(("admin1", AuditResult.Success, "PLC-04"), (row.Actor, row.Result, row.TargetId));
        Assert.Contains("\"approved\":", row.AfterJson ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(connBefore, await h.Db(db => db.PlcConnections.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.Enabled, c.IpAddress, c.Port, c.Protocol }).ToListAsync()));
        Assert.Equal(modeBefore, (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Root.GetProperty("data").GetProperty("runtime").GetProperty("effectiveMode").GetString());
        Assert.Equal(0, await h.Db(db => db.StationIoPoints.CountAsync(p => p.WriteAllowed)));                                 // read permission only
        Assert.Equal(0, await h.Db(db => db.PlcCommLog.CountAsync(l => l.Event == "WRITE" || l.Event == "WRITE_AMBIGUOUS" || l.Event == "WRITE_FAILED")));   // no PLC write
    }

    [Fact]
    public async Task Operators_and_anonymous_callers_cannot_use_it_and_nothing_changes()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        await EnterAddressesAsync(h, admin, "PLC-04", "E1", 3, 0);
        var op = await As(f, "op1");
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Get, Url("PLC-04"))).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, Url("PLC-04"), new { confirm = true, expectedEligible = 3 })).Status);
        Assert.Equal(401, (await f.CreateClient().WithClientHeader().SendJsonAsync(HttpMethod.Post, Url("PLC-04"), new { confirm = true, expectedEligible = 3 })).Status);
        Assert.Equal(0, await h.Db(db => db.StationIoPoints.CountAsync(p => p.Status == IoConfigStatus.Approved)));
    }

    [Fact]
    public async Task Only_the_allowed_PLC_can_be_bulk_approved_the_confirmation_is_required_and_a_stale_preview_is_refused()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        await EnterAddressesAsync(h, admin, "PLC-04", "E1", 5, 0);
        await EnterAddressesAsync(h, admin, "PLC-02", "B1", 5, 200);

        var other = await admin.SendJsonAsync(HttpMethod.Post, Url("PLC-02"), new { confirm = true, expectedEligible = 5 });
        Assert.Equal(422, other.Status); Assert.Equal("BULK_APPROVAL_NOT_ALLOWED", other.Root.ErrorCode());
        Assert.Equal(422, (await admin.SendJsonAsync(HttpMethod.Get, Url("PLC-02"))).Status);
        Assert.Equal(404, (await admin.SendJsonAsync(HttpMethod.Post, Url("PLC-99"), new { confirm = true, expectedEligible = 0 })).Status);

        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Post, Url("PLC-04"), new { })).Status);                          // no confirmation
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Post, Url("PLC-04"), new { confirm = false, expectedEligible = 5 })).Status);
        var stale = await admin.SendJsonAsync(HttpMethod.Post, Url("PLC-04"), new { confirm = true, expectedEligible = 4 });        // the preview said 4, now there are 5
        Assert.Equal(422, stale.Status); Assert.Equal("BULK_APPROVAL_PREVIEW_STALE", stale.Root.ErrorCode());

        Assert.Equal(0, await h.Db(db => db.StationIoPoints.CountAsync(p => p.Status == IoConfigStatus.Approved)));              // every refusal left the data alone
        Assert.Contains(await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action == "PLC_IO_BULK_APPROVE").Select(a => a.Result).ToListAsync()), x => x != AuditResult.Success);   // refusals are audited too
    }

    [Fact]
    public async Task The_allowed_list_is_configuration_not_code()
    {
        var (h, f) = await ArrangeAsync(new() { ["Plc:BulkApproval:AllowedPlcIds:0"] = "PLC-03" });
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        await EnterAddressesAsync(h, admin, "PLC-03", "X1", 2, 0);
        await EnterAddressesAsync(h, admin, "PLC-04", "E1", 2, 50);
        Assert.Equal("BULK_APPROVAL_NOT_ALLOWED", (await admin.SendJsonAsync(HttpMethod.Post, Url("PLC-04"), new { confirm = true, expectedEligible = 2 })).Root.ErrorCode());
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, Url("PLC-03"), new { confirm = true, expectedEligible = 2 })).Status);
        Assert.Equal(2, await h.Db(db => db.StationIoPoints.CountAsync(p => p.PlcId == "PLC-03" && p.Status == IoConfigStatus.Approved)));
    }

    [Fact]
    public async Task A_point_that_is_not_approved_when_re_read_is_reported_failed_and_the_others_are_kept()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        var entered = await EnterAddressesAsync(h, admin, "PLC-04", "E1", 4, 0);

        using var scopeA = f.Services.CreateScope();
        var svc = scopeA.ServiceProvider.GetRequiredService<PlcConfigService>();
        var applied = await svc.BulkApproveAsync("PLC-04", "admin1", BulkApprovalPolicy.Default, 4);
        Assert.Equal(4, applied.Approved);
        // a concurrent edit withdraws one approval between the commit and the check
        await h.Db(async db => { var p = await db.StationIoPoints.SingleAsync(x => x.Id == entered[1].Id); p.ClearAddress(DateTimeOffset.UtcNow); await db.SaveChangesAsync(); return 0; });

        using var scopeB = f.Services.CreateScope();
        var verified = await scopeB.ServiceProvider.GetRequiredService<PlcConfigService>().VerifyBulkApprovalAsync(applied);
        Assert.Equal((3, 1, 3), (verified.Approved, verified.Failed, verified.VerifiedPollable));
        var failed = Assert.Single(verified.Items, i => i.Outcome == BulkOutcome.Failed);
        Assert.Equal((entered[1].Id, "VERIFY_FAILED"), (failed.PointId, failed.Code));
    }

    [Fact]
    public async Task Running_it_twice_is_idempotent()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        await EnterAddressesAsync(h, admin, "PLC-04", "E1", 6, 0);
        Assert.Equal(6, (await admin.SendJsonAsync(HttpMethod.Post, Url("PLC-04"), new { confirm = true, expectedEligible = 6 })).Root.GetProperty("data").GetProperty("approved").GetInt32());
        var at = await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.Status == IoConfigStatus.Approved).Select(p => p.ApprovedAt).ToListAsync());
        var again = (await admin.SendJsonAsync(HttpMethod.Post, Url("PLC-04"), new { confirm = true, expectedEligible = 0 })).Root.GetProperty("data");
        Assert.Equal((0, 6, 0), (again.GetProperty("approved").GetInt32(), again.GetProperty("alreadyApproved").GetInt32(), again.GetProperty("failed").GetInt32()));
        Assert.Equal(at.Order(), (await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.Status == IoConfigStatus.Approved).Select(p => p.ApprovedAt).ToListAsync())).Order());
    }
}
