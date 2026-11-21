using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Application.Plc;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Locations;
using WCS.Domain.Plc;
using WCS.Domain.Routing;
using WCS.Infrastructure.Persistence;

namespace WCS.Tests.Support;

/// <summary>Controllable PLC link state (every PLC is online unless a test switches it off).</summary>
public sealed class FakeLinkStatus : IPlcLinkStatus
{
    public HashSet<string> Offline { get; } = [];
    public bool? IsOnline(string plcId) => !Offline.Contains(plcId);
}

/// <summary>Returns whatever observation the test sets for a port (null = no data).</summary>
public sealed class FakeObservationSource : IPlcObservationSource
{
    public Dictionary<string, PlcHandshakeObservation?> ByPort { get; } = [];
    public Task<PlcHandshakeObservation?> ReadAsync(string plcId, string portId, CancellationToken ct = default) =>
        Task.FromResult(ByPort.TryGetValue(portId, out var o) ? o : null);
}

/// <summary>Builders and helpers for the CR-010 tests. All data is created through the real services and the real PostgreSQL schema.</summary>
public sealed class RoutingKit : IAsyncDisposable
{
    public Harness H { get; }
    public FakeLinkStatus Link { get; }
    public FakeObservationSource Observations { get; }

    private RoutingKit(Harness h, FakeLinkStatus link, FakeObservationSource obs) { H = h; Link = link; Observations = obs; }

    public static async Task<RoutingKit> CreateAsync(DatabaseFixture db, bool seedPorts = true, bool approvePorts = true, Action<IServiceCollection>? more = null, IReadOnlyDictionary<string, string?>? extraConfig = null)
    {
        var link = new FakeLinkStatus(); var obs = new FakeObservationSource();
        var h = await Harness.CreateAsync(db, s => { s.AddSingleton<IPlcLinkStatus>(link); s.AddSingleton<IPlcObservationSource>(obs); more?.Invoke(s); }, extraConfig);
        await h.SeedStandardAsync(bufferCapacity: 3, stationCapacity: 1);
        await h.Db(async d => { d.Locations.Add(new Location("C1", "C1", LocationType.Station, 1, AllowedRackKind.Any, true, h.Clock.UtcNow)); await d.SaveChangesAsync(); return 0; });
        var kit = new RoutingKit(h, link, obs);
        if (seedPorts) await kit.SeedPortsAsync(approvePorts);
        return kit;
    }

    /// <summary>PLC-T1 serves A1 and A2; PLC-T2 serves B1, B2 and C1 (so there are two ports on one PLC and ports on different PLCs). One IO point per port; Approved when asked.</summary>
    public async Task SeedPortsAsync(bool approve)
    {
        await H.Db(async db =>
        {
            var now = H.Clock.UtcNow;
            foreach (var plc in new[] { "PLC-T1", "PLC-T2" })
            {
                var c = new PlcConnection(plc, plc, "10.0.0.1", now);
                c.Update(plc, "10.0.0.1", null, null, null, null, null, null, null, null, null, true, "test", now);
                db.PlcConnections.Add(c);
            }
            await db.SaveChangesAsync();
            var order = 0;
            foreach (var (plc, port) in new[] { ("PLC-T1", "A1"), ("PLC-T1", "A2"), ("PLC-T2", "B1"), ("PLC-T2", "B2"), ("PLC-T2", "C1") })
            {
                var st = new PlcStation(port, plc, ++order, now); st.Update(port, true, now); db.PlcStations.Add(st);
            }
            await db.SaveChangesAsync();
            var addr = 1000;
            foreach (var (plc, port) in new[] { ("PLC-T1", "A1"), ("PLC-T1", "A2"), ("PLC-T2", "B1"), ("PLC-T2", "B2"), ("PLC-T2", "C1") })
            {
                var p = new StationIoPoint(plc, port, 1, "Handshake", "Request_Ack", PlcSignalDirection.SoftwareToPlc, PlcDataType.Bool, "M9101", "test", now);
                p.ConfigureAddress($"M{++addr}", null, null, now);   // distinct per port: an address is unique within a PLC
                if (approve) p.Approve("engineer", now);
                db.StationIoPoints.Add(p);
            }
            await db.SaveChangesAsync();
            return 0;
        });
    }

    public Task<T> With<T>(Func<IServiceProvider, Task<T>> f) => H.Scoped(f);
    public Task Run(Func<IServiceProvider, Task> f) => H.RunAsync(f);

    // ------------------------------------------------------------------ process administration
    public async Task<(Guid ProcessId, Guid VersionId)> CreateProcessAsync(string code, RequestType type, RackKind kind, string[] triggers, VersionDefinition def)
    {
        return await With(async sp =>
        {
            var admin = sp.GetRequiredService<ProcessAdminService>();
            var p = await admin.CreateAsync(new ProcessHeader(code, code, null, type.ToString(), kind.ToString(), triggers), "test");
            await admin.SaveDefinitionAsync(p.Versions[0].Id, def);
            return (p.Id, p.Versions[0].Id);
        });
    }

    public Task<VersionDto> ValidateAsync(Guid versionId) => With(sp => sp.GetRequiredService<ProcessAdminService>().ValidateAsync(versionId));

    public async Task<VersionDto> PublishAsync(Guid versionId)
    {
        var v = await ValidateAsync(versionId);
        Assert.True(v.State == "Validated", "validation failed: " + string.Join("; ", v.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => $"{i.Code}@{i.StepKey}")));
        return await With(sp => sp.GetRequiredService<ProcessAdminService>().PublishAsync(versionId, "test"));
    }

    public async Task<Dictionary<string, (Guid ProcessId, Guid VersionId)>> ImportReferenceAsync()
    {
        await With(sp => sp.GetRequiredService<ProcessAdminService>().ImportReferenceAsync(ReferenceProcessCatalog.Load(), "test"));
        return await H.Db(async db =>
        {
            var procs = await db.BusinessProcesses.AsNoTracking().ToListAsync();
            var vers = await db.BusinessProcessVersions.AsNoTracking().ToListAsync();
            return procs.ToDictionary(p => p.Code, p => (p.Id, vers.First(v => v.ProcessId == p.Id).Id));
        });
    }

    public async Task PublishReferenceAsync(params string[] codes)
    {
        var all = await ImportReferenceAsync();
        foreach (var c in codes) await PublishAsync(all[c].VersionId);
    }

    // ------------------------------------------------------------------ engine
    public Task<StartOrderResult> StartAsync(string key, string location, RequestType type, RackKind? kind, string? processCode = null) =>
        With(sp => sp.GetRequiredService<IRouteEngine>().StartAsync(new StartOrderCommand(key, location, type, kind, processCode, "test")));

    public Task<EventResult> PickAsync(Guid legId) => With(sp => sp.GetRequiredService<IRouteEngine>().ConfirmPickAsync(legId, ConfirmationSource.Simulator, "test"));
    public Task<EventResult> DropAsync(Guid legId, string? externalRackId = null) => With(sp => sp.GetRequiredService<IRouteEngine>().ConfirmDropAsync(legId, ConfirmationSource.Simulator, "test", externalRackId));
    public Task TickAsync() => With(async sp => await sp.GetRequiredService<IRouteEngine>().RunOnceAsync());
    public Task<int> ExitAsync(Guid legId, string location) => With(sp => sp.GetRequiredService<StationTransactionService>().ConfirmAgvExitedForLegAsync(legId, location));

    public Task<PlcRequestResult> PlcRequestAsync(string plc, string? port, string requestId, bool pickup, int? rackFull = null, int? rackEmpty = null, bool delivery = false) =>
        With(sp => sp.GetRequiredService<IPlcRequestService>().HandleRequestAsync(new PlcRequestSnapshot(plc, port, requestId, pickup, delivery, rackFull, rackEmpty, null, null)));

    // ------------------------------------------------------------------ inspection
    public Task<TransportOrder> OrderAsync(Guid id) => H.Db(db => db.TransportOrders.Include(o => o.Legs).AsNoTracking().SingleAsync(o => o.Id == id));
    public Task<List<StationTransaction>> TransactionsAsync(Guid? orderId = null) =>
        H.Db(db => db.StationTransactions.AsNoTracking().Where(t => orderId == null || t.OrderId == orderId).OrderBy(t => t.CreatedAt).ToListAsync());
    public Task<List<WCS.Domain.Inventory.Rack>> RacksAsync() => H.Db(db => db.Racks.AsNoTracking().ToListAsync());
    public Task<WCS.Domain.Inventory.Rack?> RackAtAsync(string location) => H.Db(async db =>
    {
        var id = (await db.Locations.AsNoTracking().SingleAsync(l => l.Code == location)).Id;
        return await db.Racks.AsNoTracking().FirstOrDefaultAsync(r => r.CurrentLocationId == id);
    });

    public ValueTask DisposeAsync() => H.DisposeAsync();

    // ------------------------------------------------------------------ definition builders
    public static ConditionDefinition Cond(string type, string? parameter = null, string? location = null) => new(type, parameter, location);

    public static CandidateDefinition Dest(int priority, string code, bool noBind = false, int order = 1) => new("Destination", priority, order, code, null, noBind);
    public static CandidateDefinition Src(int priority, string code, int order = 1) => new("Source", priority, order, code);

    public static StepDefinition Move(string key, int order, string type, string source, string dest, string? next, IReadOnlyList<CandidateDefinition> cands,
        string resolution = "BeforePick", string wait = "Wait", string? alt = null) =>
        new(key, order, type, source, dest, resolution, next, alt, wait, "Fail", null, null, null, cands);

    public static StepDefinition Done(string key = "done", int order = 99) => new(key, order, "ProcessCompletion", null, null, null, null, null, null, null, null, null, null, null);

    /// <summary>A simple one-leg process: pick at the request location, deliver to the first usable destination.</summary>
    public static VersionDefinition OneLeg(string source, params CandidateDefinition[] dests) =>
        new("s1", [Move("s1", 1, "Delivery", source, "Candidates", "done", dests), Done()]);

    /// <summary>
    /// Multi-leg test process (the shape the owner described): A -> B when B has room, else store at a buffer, wait there until B has room, then buffer -> B.
    /// Everything is data: nothing in the engine knows these location names.
    /// </summary>
    public static VersionDefinition MultiLeg(string primary, string buffer) => new("s1",
    [
        Move("s1", 1, "Delivery", "Request", "Candidates", "done", [Dest(1, primary)], wait: "Alternative", alt: "s2"),
        Move("s2", 2, "BufferStorage", "Request", "Candidates", "w1", [Dest(1, buffer)]),
        new StepDefinition("w1", 3, "Waiting", null, null, null, "s3", null, null, null, [Cond("InventoryState", "HasFreeSlot", primary)], null, null, null),
        Move("s3", 4, "BufferRetrieval", "PreviousDestination", "Candidates", "done", [Dest(1, primary)]),
        Done(),
    ]);
}
