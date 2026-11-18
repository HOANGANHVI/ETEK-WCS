using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Plc;
using WCS.Application.Services;
using WCS.Domain.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Infrastructure.Plc.Mc;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Plc;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-012: an entered, valid address makes an IO point pollable (no approval). Writing, the handshake and the connection guards stay separate.</summary>
[Collection("Database")]
public class PlcPollingPolicyTests(DatabaseFixture fixture)
{
    private async Task<(Harness H, WebApplicationFactory<Program> F)> ArrangeAsync()
    {
        var h = await Harness.CreateAsync(fixture);
        var f = new DbApiFactory(fixture);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        db.Users.Add(TestUsers.Make("admin1", UserRole.Admin));
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>().SeedAsync();
        return (h, f);
    }

    private static async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> f)
    {
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync("admin1"));
        return c;
    }

    private static Task<Guid> Id(Harness h, string plc, string station, string signal) =>
        h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.PlcId == plc && p.StationId == station && p.SignalName == signal).Select(p => p.Id).SingleAsync());

    private static async Task<bool> PollableAsync(HttpClient admin, Guid id)
    {
        var all = (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/points")).Root.GetProperty("data");
        return all.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id).GetProperty("pollable").GetBoolean();
    }

    // ---------------------------------------------------------------- 1-7: the address decides
    [Fact]
    public async Task A_valid_address_is_pollable_at_once_an_invalid_empty_or_cleared_one_is_not_and_approval_plays_no_part()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var admin = await AdminAsync(f);
        var id = await Id(h, "PLC-04", "E1", "Pickup_Request");
        Assert.False(await PollableAsync(admin, id));                                                                    // nothing entered

        var set = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/address", new { address = "M9001" });
        Assert.Equal(200, set.Status);
        Assert.True(set.Root.GetProperty("data").GetProperty("pollable").GetBoolean());                                  // 1. valid -> pollable
        Assert.True(await PollableAsync(admin, id));                                                                     // the list agrees
        var stored = await h.Db(db => db.StationIoPoints.AsNoTracking().SingleAsync(p => p.Id == id));
        Assert.Equal((IoConfigStatus.Configured, null, false), (stored.Status, stored.ApprovedBy, stored.WriteAllowed));   // 7. never approved, still pollable

        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/address", new { address = "" })).Status);           // 2. empty: refused...
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/address", new { address = "ZZ9" })).Status);        // 3. bad format: refused with an error
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/address", new { address = "D100" })).Status);       // BOOL on a word device
        Assert.True(await PollableAsync(admin, id));                                                                     // ...and the refused calls changed nothing
        Assert.Equal("M9001", await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.Id == id).Select(p => p.ActualAddress).SingleAsync()));

        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/address", new { address = "M9010" })).Status);       // 5. change to another valid one
        Assert.True(await PollableAsync(admin, id));

        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Delete, $"/api/plc/points/{id}/address")).Status);       // 4. clear
        Assert.False(await PollableAsync(admin, id));
    }

    [Fact]
    public async Task Conflicting_addresses_are_refused_on_entry_and_legacy_conflicts_in_the_data_are_not_polled()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var admin = await AdminAsync(f);
        var a = await Id(h, "PLC-04", "E1", "Pickup_Request"); var b = await Id(h, "PLC-04", "E1", "Delivery_Request"); var other = await Id(h, "PLC-03", "X1", "Pickup_Request");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{a}/address", new { address = "M9001" })).Status);
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{b}/address", new { address = "M9001" })).Status);   // 6. same PLC: refused on entry
        Assert.False(await PollableAsync(admin, b));
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{other}/address", new { address = "M9001" })).Status);   // another PLC has its own namespace

        // two registers that overlap (a STRING of 10 words from D1995 covers D2000): refused on entry
        var word = await Id(h, "PLC-04", "E1", "Rack_Full"); var text = await Id(h, "PLC-04", "E1", "Request_ID");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{word}/address", new { address = "D2000" })).Status);
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{text}/address", new { address = "D1995", wordLength = 10, encoding = "ASCII" })).Status);
        Assert.False(await PollableAsync(admin, text));

        // data that bypassed the entry validation (an old import, a manual change): the policy still keeps both overlapping points out of the polling
        await h.Db(async db =>
        {
            (await db.StationIoPoints.SingleAsync(p => p.Id == text)).ConfigureAddress("D1995", 10, "ASCII", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(); return 0;
        });
        Assert.False(await PollableAsync(admin, word)); Assert.False(await PollableAsync(admin, text));
        Assert.True(await PollableAsync(admin, a));                                                                      // an unrelated point of the same PLC
        Assert.True(await PollableAsync(admin, other));                                                                  // another PLC: untouched
        var rows = await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.PlcId == "PLC-04").ToListAsync());
        var ok = PlcPollablePolicy.Pollable(rows, AddressBases.None);                                                    // what the runtime uses
        Assert.DoesNotContain(word, ok); Assert.DoesNotContain(text, ok); Assert.Contains(a, ok);
    }

    // ---------------------------------------------------------------- 8: polling on, PLC unreachable
    [Fact]
    public async Task A_pollable_point_of_an_unreachable_PLC_is_never_reported_as_connected_or_good()
    {
        int port; { await using var s = new MelsecMcMockServer { ExpectedRoute = new McRoute(0, 0xFF, 0x03FF, 0) }; s.Start(); port = s.Port; }   // a port nobody listens on
        var point = new IoPointRef(Guid.NewGuid(), "PLC-04", "E1", "Pickup_Request", PlcSignalDirection.PlcToSoftware, PlcDataType.Bool, "M9001", null, null, false, "Status", true);
        var handler = new StationIoHandler("PLC-04", "E1", [new PointInfo(point, 1, IoConfigStatus.Configured)]);                          // pollable, never approved
        var opts = new PlcRuntimeOptions { Mode = PlcRuntimeMode.RealReadOnly, AllowLiveConnection = true, ReconnectInitialDelayMs = 40, ReconnectMaxDelayMs = 160 };
        var cfg = new PlcEndpointConfig("PLC-04", "127.0.0.1", port, "McProtocol3EBinaryTcp", 0, 0xFF, 0x03FF, 0, 0x0010, 300, 25, 5000, 300, 300);
        await using var w = new PlcWorker("PLC-04", "PLC-04", PlcRuntimeMode.RealReadOnly, opts, cfg, true, null, [handler], new McTestFactory(), new FakeEvents(), new SystemClock());
        w.Start();
        var until = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < until && w.Status(true).State != "Reconnecting" && w.Status(true).State != "Faulted") await Task.Delay(50);
        await Task.Delay(400);
        Assert.NotEqual("Connected", w.Status(true).State);
        var q = w.Stations.Single().Snapshot(true).Points.Single();
        Assert.NotEqual("Good", q.Quality); Assert.Null(q.Value);
        Assert.Equal(0, w.Stations.Single().Snapshot(true).Good);
    }

    // ---------------------------------------------------------------- 9-10: write and handshake stay protected
    [Fact]
    public async Task Pollable_does_not_grant_write_permission_and_the_write_policy_still_blocks_writes()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var admin = await AdminAsync(f);
        var ack = await Id(h, "PLC-04", "E1", "Request_Ack");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{ack}/address", new { address = "M9100" })).Status);
        Assert.True(await PollableAsync(admin, ack));
        var blocked = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{ack}/write-allowed", new { allowed = true });
        Assert.Equal(422, blocked.Status); Assert.Equal("IO_NOT_APPROVED", blocked.Root.ErrorCode());                    // write permission still needs the approval
        Assert.False(await h.Db(db => db.StationIoPoints.Where(p => p.Id == ack).Select(p => p.WriteAllowed).SingleAsync()));

        // even an approved, write-allowed, pollable point is refused by the policy in the read-only / degraded modes
        var p = new IoPointRef(ack, "PLC-04", "E1", "Request_Ack", PlcSignalDirection.SoftwareToPlc, PlcDataType.Bool, "M9100", null, null, WriteAllowed: true, "Handshake", Pollable: true);
        Assert.Equal("READ_ONLY", WritePolicy.Evaluate(new PlcRuntimeOptions { Mode = PlcRuntimeMode.RealReadOnly, AllowLiveConnection = true }, p).Code);
        Assert.Equal("READ_ONLY", WritePolicy.Evaluate(new PlcRuntimeOptions { Mode = PlcRuntimeMode.RealReadWrite, AllowLiveConnection = true }, p).Code);   // write keys incomplete -> degraded
        Assert.False(WritePolicy.Evaluate(new PlcRuntimeOptions { Mode = PlcRuntimeMode.RealReadOnly }, p with { SignalName = "Transfer_Permit" }).Allowed);
        Assert.Equal(0, await h.Db(db => db.PlcCommLog.CountAsync(l => l.Event == "WRITE" || l.Event == "WRITE_AMBIGUOUS" || l.Event == "WRITE_FAILED")));
    }

    [Fact]
    public async Task Pollable_points_do_not_approve_the_handshake()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var admin = await AdminAsync(f);
        var sigs = new[] { "Pickup_Request", "Delivery_Request", "Station_Online", "Request_Ack" };
        foreach (var s in sigs)
            Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{await Id(h, "PLC-04", "E1", s)}/address", new { address = $"M{9300 + Array.IndexOf(sigs, s)}" })).Status);
        await f.Services.GetRequiredService<IPlcRuntime>().StartAsync();

        var hs = (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/handshake")).Root.GetProperty("data");
        Assert.True(hs.GetArrayLength() > 0);
        Assert.All(hs.EnumerateArray(), s => { Assert.Equal("NEEDS_CONFIRMATION", s.GetProperty("approval").GetString()); Assert.Equal(0, s.GetProperty("approvedRules").GetInt32()); });
        Assert.Equal(0, await f.Services.GetRequiredService<HandshakeCoordinator>().StepAsync());                       // no approved rule -> nothing is written
    }
}
