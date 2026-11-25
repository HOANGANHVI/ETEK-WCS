using Microsoft.EntityFrameworkCore;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Plc;
using WCS.Domain.Routing;

namespace WCS.Infrastructure.Persistence.Repositories;

internal sealed class RoutingRepository(WcsDbContext db) : IRoutingRepository
{
    private static readonly StationTransactionState[] FinalStates = [StationTransactionState.Completed, StationTransactionState.Failed];

    // ---------------------------------------------------------------- processes
    public Task<BusinessProcess?> GetProcessAsync(Guid id, CancellationToken ct) => db.BusinessProcesses.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<BusinessProcess?> GetProcessByCodeAsync(string code, CancellationToken ct) => db.BusinessProcesses.FirstOrDefaultAsync(x => x.Code == code, ct);
    public async Task<IReadOnlyList<BusinessProcess>> ListProcessesAsync(CancellationToken ct) => await db.BusinessProcesses.OrderBy(x => x.Code).ToListAsync(ct);
    public void Add(BusinessProcess process) => db.BusinessProcesses.Add(process);

    public Task<BusinessProcessVersion?> GetVersionAsync(Guid id, CancellationToken ct) =>
        db.BusinessProcessVersions.Include(v => v.Steps).ThenInclude(s => s.Candidates).FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<IReadOnlyList<BusinessProcessVersion>> ListVersionsAsync(Guid processId, CancellationToken ct) =>
        await db.BusinessProcessVersions.Include(v => v.Steps).Where(v => v.ProcessId == processId).OrderBy(v => v.Number).ToListAsync(ct);

    public Task<BusinessProcessVersion?> GetPublishedVersionAsync(Guid processId, CancellationToken ct) =>
        db.BusinessProcessVersions.Include(v => v.Steps).ThenInclude(s => s.Candidates)
            .FirstOrDefaultAsync(v => v.ProcessId == processId && v.State == ProcessConfigState.Published, ct);

    public async Task<bool> IsLocationReferencedByPublishedAsync(string locationCode, CancellationToken ct)
    {
        var published = db.BusinessProcessVersions.Where(v => v.State == ProcessConfigState.Published);
        var triggers = await (from v in published join p in db.BusinessProcesses on v.ProcessId equals p.Id select p.TriggerLocationCodesCsv).ToListAsync(ct);
        if (triggers.Any(csv => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(locationCode, StringComparer.OrdinalIgnoreCase))) return true;
        var steps = db.RouteSteps.Where(s => published.Any(v => v.Id == s.VersionId));
        if (await steps.AnyAsync(s => s.WaitingPositionCode == locationCode || (s.ConditionsJson != null && s.ConditionsJson.Contains(locationCode)), ct)) return true;
        return await db.RouteCandidates.AnyAsync(c => steps.Any(s => s.Id == c.StepId) && (c.LocationCode == locationCode || (c.ConditionsJson != null && c.ConditionsJson.Contains(locationCode))), ct);
    }

    public async Task<IReadOnlyList<(BusinessProcess Process, BusinessProcessVersion Version)>> FindPublishedAsync(RequestType type, RackKind? kind, string locationCode, CancellationToken ct)
    {
        var q = db.BusinessProcesses.Where(p => p.Enabled && p.RequestType == type);
        if (kind is { } k) q = q.Where(p => p.RackKind == k);
        var processes = (await q.ToListAsync(ct)).Where(p => p.TriggerLocationCodes.Contains(locationCode, StringComparer.OrdinalIgnoreCase)).ToList();
        var result = new List<(BusinessProcess, BusinessProcessVersion)>();
        foreach (var p in processes)
            if (await GetPublishedVersionAsync(p.Id, ct) is { } v) result.Add((p, v));
        return result;
    }

    public void Add(BusinessProcessVersion version) => db.BusinessProcessVersions.Add(version);

    // ---------------------------------------------------------------- orders / legs
    public Task<TransportOrder?> GetOrderAsync(Guid id, CancellationToken ct) => db.TransportOrders.Include(o => o.Legs).FirstOrDefaultAsync(o => o.Id == id, ct);
    public Task<TransportOrder?> GetOrderByRequestKeyAsync(string requestKey, CancellationToken ct) => db.TransportOrders.Include(o => o.Legs).FirstOrDefaultAsync(o => o.RequestKey == requestKey, ct);
    public void Add(TransportOrder order) => db.TransportOrders.Add(order);
    public void Add(TransportLeg leg) { if (db.Entry(leg).State == EntityState.Detached) db.TransportLegs.Add(leg); }
    public Task<TransportLeg?> GetLegAsync(Guid id, CancellationToken ct) => db.TransportLegs.FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task<TransportLeg?> GetActiveLegForAgvAsync(Guid agvId, CancellationToken ct) =>
        db.TransportLegs.FirstOrDefaultAsync(l => l.AgvId == agvId && (l.State == LegState.InProgress || l.State == LegState.Waiting), ct);

    public Task<int> CountInFlightLegsByModeAsync(bool rcsManaged, CancellationToken ct) => rcsManaged
        ? db.TransportLegs.CountAsync(l => l.AgvId == null && (l.AgvTaskCode != null || l.State == LegState.InProgress || l.State == LegState.Waiting)
                                           && l.State != LegState.Completed && l.State != LegState.Failed && l.State != LegState.Cancelled, ct)
        : db.TransportLegs.CountAsync(l => l.AgvId != null && l.State != LegState.Completed && l.State != LegState.Failed && l.State != LegState.Cancelled, ct);

    public async Task<IReadOnlyList<Guid>> ListManagedLegIdsToSyncAsync(int take, CancellationToken ct) =>
        await db.TransportLegs.AsNoTracking()
            .Where(l => l.AgvId == null && l.AgvTaskCode != null && l.State != LegState.Completed && l.State != LegState.Failed && l.State != LegState.Cancelled)
            .OrderBy(l => l.CreatedAt).Select(l => l.Id).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListOpenOrderIdsAsync(int take, CancellationToken ct) =>
        await db.TransportOrders.AsNoTracking()
            .Where(o => o.State == OrderState.Created || o.State == OrderState.Accepted || o.State == OrderState.InProgress || o.State == OrderState.Waiting)
            .OrderBy(o => o.CreatedAt).Select(o => o.Id).Take(take).ToListAsync(ct);

    public async Task LockQueueAsync(string key, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"wcs-queue:" + key}))", ct);

    public async Task<long> NextQueueSequenceAsync(CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<long>("SELECT nextval('transport_queue_seq') AS \"Value\"").ToListAsync(ct))[0];

    /// <summary>Open orders of a scope that are older than the sequence. An order not yet pinned counts by the scope its process declares now.</summary>
    private IQueryable<TransportOrder> OlderInScope(string scope, long sequence) =>
        db.TransportOrders.Where(o => o.QueueSequence != null && o.QueueSequence < sequence
            && o.State != OrderState.Completed && o.State != OrderState.Failed && o.State != OrderState.Cancelled && o.State != OrderState.ReconciliationRequired
            && (o.QueueScope == scope || (o.QueueScope == null && db.BusinessProcesses.Any(p => p.Id == o.ProcessId && p.QueueScope == scope))));

    public Task<TransportOrder?> FindOlderWaitingInScopeAsync(string scope, long sequence, CancellationToken ct) =>
        OlderInScope(scope, sequence)
            .Where(o => o.LegCount == 0
                || !o.Legs.Any(l => l.State != LegState.Completed && l.State != LegState.Failed && l.State != LegState.Cancelled)
                || o.Legs.Any(l => l.DestinationLocationCode == null && l.State != LegState.Completed && l.State != LegState.Failed && l.State != LegState.Cancelled))
            .OrderBy(o => o.QueueSequence).AsNoTracking().FirstOrDefaultAsync(ct);

    public Task<bool> HasOlderUnsentLegInScopeAsync(string scope, long sequence, CancellationToken ct) =>
        OlderInScope(scope, sequence).AnyAsync(o => o.Legs.Any(l => l.AgvTaskCode == null && (l.State == LegState.Ready || l.State == LegState.InProgress) && l.PickConfirmedAt == null), ct);

    public async Task<IReadOnlyList<Guid>> ListReadyLegIdsAsync(int take, CancellationToken ct) =>
        await db.TransportLegs.AsNoTracking()
            .Where(l => l.State == LegState.Ready || (l.State == LegState.InProgress && l.AgvTaskCode == null))
            .OrderBy(l => l.CreatedAt).Select(l => l.Id).Take(take).ToListAsync(ct);

    public async Task<(IReadOnlyList<TransportOrder> Items, int Total)> QueryOrdersAsync(OrderState? state, int skip, int take, CancellationToken ct)
    {
        var q = db.TransportOrders.AsNoTracking().Include(o => o.Legs).AsQueryable();
        if (state is { } s) q = q.Where(o => o.State == s);
        var total = await q.CountAsync(ct);
        return (await q.OrderByDescending(o => o.CreatedAt).Skip(Math.Max(0, skip)).Take(take).ToListAsync(ct), total);
    }

    public void Add(RoutingEvent e) => db.RoutingEvents.Add(e);
    public Task FlushAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<RoutingEvent>> ListEventsAsync(Guid orderId, int take, CancellationToken ct) =>
        await db.RoutingEvents.AsNoTracking().Where(e => e.OrderId == orderId).OrderBy(e => e.OccurredAt).Take(take).ToListAsync(ct);

    // ---------------------------------------------------------------- station transactions
    public Task<StationTransaction?> GetTransactionAsync(Guid id, CancellationToken ct) => db.StationTransactions.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<StationTransaction?> GetActiveTransactionForPortAsync(string portId, CancellationToken ct) =>
        db.StationTransactions.FirstOrDefaultAsync(x => x.PortId == portId && !FinalStates.Contains(x.State), ct);

    public Task<StationTransaction?> GetTransactionByRequestIdAsync(string plcId, string requestId, CancellationToken ct) =>
        db.StationTransactions.FirstOrDefaultAsync(x => x.PlcId == plcId && x.RequestId == requestId, ct);

    public async Task<IReadOnlyList<StationTransaction>> ListTransactionsAsync(bool activeOnly, string? plcId, Guid? orderId, int take, CancellationToken ct)
    {
        var q = db.StationTransactions.AsQueryable();
        if (activeOnly) q = q.Where(x => !FinalStates.Contains(x.State));
        if (plcId is not null) q = q.Where(x => x.PlcId == plcId);
        if (orderId is not null) q = q.Where(x => x.OrderId == orderId);
        return await q.OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(ct);
    }

    public void Add(StationTransaction tx) => db.StationTransactions.Add(tx);

    // ---------------------------------------------------------------- ports
    public Task<PlcStation?> FindPortByLocationAsync(string locationCode, CancellationToken ct) =>
        db.PlcStations.FirstOrDefaultAsync(s => s.LocationCode == locationCode && s.Enabled, ct);

    public Task<PlcStation?> GetPortAsync(string portId, CancellationToken ct) => db.PlcStations.FirstOrDefaultAsync(s => s.Id == portId, ct);

    public async Task<bool> IsPortMappingApprovedAsync(string portId, CancellationToken ct)
    {
        var statuses = await db.StationIoPoints.AsNoTracking().Where(p => p.StationId == portId).Select(p => p.Status).ToListAsync(ct);
        return statuses.Count > 0 && statuses.All(s => s == IoConfigStatus.Approved);
    }

    public async Task<IReadOnlyList<PlcStation>> ListLinkedPortsAsync(CancellationToken ct) =>
        await db.PlcStations.AsNoTracking().Where(s => s.Enabled && s.LocationCode != null).OrderBy(s => s.PlcId).ThenBy(s => s.Order).ToListAsync(ct);

    public async Task<IReadOnlyList<StationIoPoint>> ListPlcInputPointsAsync(string stationId, CancellationToken ct) =>
        await db.StationIoPoints.AsNoTracking().Where(p => p.StationId == stationId && p.Direction == PlcSignalDirection.PlcToSoftware).OrderBy(p => p.Seq).ToListAsync(ct);

    public Task<string?> FindOpenOrderUsingRackAsync(Guid rackId, CancellationToken ct) =>
        db.TransportOrders.AsNoTracking()
            .Where(o => o.State != OrderState.Completed && o.State != OrderState.Failed && o.State != OrderState.Cancelled
                        && (o.RackId == rackId || o.Legs.Any(l => l.RackId == rackId && l.State != LegState.Completed && l.State != LegState.Failed && l.State != LegState.Cancelled)))
            .Select(o => (string?)o.RequestKey).FirstOrDefaultAsync(ct);

    public Task<bool> IsPlcEnabledAsync(string plcId, CancellationToken ct) => db.PlcConnections.AsNoTracking().Where(c => c.Id == plcId).Select(c => c.Enabled).FirstOrDefaultAsync(ct);
}
