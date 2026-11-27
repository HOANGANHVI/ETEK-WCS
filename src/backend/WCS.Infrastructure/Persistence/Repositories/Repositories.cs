using Microsoft.EntityFrameworkCore;
using WCS.Application.Abstractions;
using WCS.Domain.Agvs;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;

namespace WCS.Infrastructure.Persistence.Repositories;

internal sealed class StationRequestRepository(WcsDbContext db) : IStationRequestRepository
{
    public Task<WCS.Domain.Intake.StationRequestRecord?> GetByKeyAsync(string requestKey, CancellationToken ct) =>
        db.StationRequests.FirstOrDefaultAsync(x => x.RequestKey == requestKey, ct);
    public async Task<IReadOnlyList<WCS.Domain.Intake.StationRequestRecord>> ListPendingAsync(int take, CancellationToken ct) =>
        await db.StationRequests.AsNoTracking().Where(x => x.Status == WCS.Domain.Intake.StationRequestStatus.Pending).OrderBy(x => x.FirstSeenAt).Take(take).ToListAsync(ct);
    public void Add(WCS.Domain.Intake.StationRequestRecord record) => db.StationRequests.Add(record);
}

internal sealed class AgvRepository(WcsDbContext db) : IAgvRepository
{
    public Task<Agv?> GetAsync(Guid id, CancellationToken ct) => db.Agvs.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<Agv?> GetByCodeAsync(string code, CancellationToken ct) => db.Agvs.FirstOrDefaultAsync(x => x.Code == code, ct);
    public Task<Agv?> GetByRoleAsync(AgvRole role, CancellationToken ct) => db.Agvs.FirstOrDefaultAsync(x => x.Role == role, ct);
    public async Task<IReadOnlyList<Agv>> ListAsync(CancellationToken ct) => await db.Agvs.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);
    public Task<bool> IsOnDispatchHoldAsync(Guid agvId, CancellationToken ct) => db.Agvs.AsNoTracking().Where(x => x.Id == agvId).Select(x => x.DispatchHold).FirstOrDefaultAsync(ct);
    public void Add(Agv agv) => db.Agvs.Add(agv);
}

internal sealed class LocationRepository(WcsDbContext db) : ILocationRepository
{
    public async Task<LocationUsage> UsageAsync(Location location, CancellationToken ct = default)
    {
        var id = location.Id; var code = location.Code; var upper = code.ToUpper(); var quoted = "\"" + code + "\"";
        var racks = await db.Racks.CountAsync(r => r.CurrentLocationId == id, ct);
        var ports = await db.PlcStations.Where(p => p.LocationCode != null && p.LocationCode.ToUpper() == upper && p.RetiredAt == null).Select(p => p.Id).OrderBy(x => x).ToListAsync(ct);
        // every version of every process: candidates, waiting positions, conditions and the trigger locations
        var steps = db.RouteSteps.Where(s => (s.WaitingPositionCode != null && s.WaitingPositionCode.ToUpper() == upper) || (s.ConditionsJson != null && s.ConditionsJson.Contains(quoted))
            || s.Candidates.Any(c => c.LocationCode.ToUpper() == upper || (c.ConditionsJson != null && c.ConditionsJson.Contains(quoted))));
        var named = await (from st in steps join v in db.BusinessProcessVersions on st.VersionId equals v.Id join p in db.BusinessProcesses on v.ProcessId equals p.Id select p.Code).ToListAsync(ct);
        var triggered = (await db.BusinessProcesses.Select(p => new { p.Code, p.TriggerLocationCodesCsv }).ToListAsync(ct))
            .Where(p => p.TriggerLocationCodesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(code, StringComparer.OrdinalIgnoreCase)).Select(p => p.Code);
        // unfinished work only: finished records keep referring to the removed row
        var open = (location.ReconciliationRequired ? 1 : 0)
            + await db.Reservations.CountAsync(x => x.LocationId == id && x.Status == ReservationStatus.Active, ct)
            + await db.Tasks.CountAsync(x => (x.RequestLocationId == id || x.SourceLocationId == id || x.DestinationLocationId == id)
                && (x.RequiresReconciliation || (x.State != TaskState.Completed && x.State != TaskState.Failed && x.State != TaskState.Cancelled)), ct)
            + await db.TransportOrders.CountAsync(x => x.RequestLocationCode.ToUpper() == upper
                && x.State != OrderState.Completed && x.State != OrderState.Failed && x.State != OrderState.Cancelled, ct)
            + await db.TransportLegs.CountAsync(x => ((x.SourceLocationCode != null && x.SourceLocationCode.ToUpper() == upper) || (x.DestinationLocationCode != null && x.DestinationLocationCode.ToUpper() == upper))
                && x.State != LegState.Completed && x.State != LegState.Failed && x.State != LegState.Cancelled, ct)
            + await db.StationTransactions.CountAsync(x => x.LocationCode.ToUpper() == upper && x.State != StationTransactionState.Completed && x.State != StationTransactionState.Failed, ct);
        return new LocationUsage(racks, ports, named.Concat(triggered).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(), open);
    }

    public async Task<IReadOnlyList<Location>> ListRemovedAsync(CancellationToken ct) => await db.Locations.AsNoTracking().Where(x => x.RemovedAt != null).OrderBy(x => x.Code).ToListAsync(ct);
    public Task<Location?> GetAsync(Guid id, CancellationToken ct) => db.Locations.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<Location?> GetByCodeAsync(string code, CancellationToken ct) => db.Locations.FirstOrDefaultAsync(x => x.Code == code && x.RemovedAt == null, ct);
    public async Task<IReadOnlyList<Location>> ListAsync(CancellationToken ct) => await db.Locations.AsNoTracking().Where(x => x.RemovedAt == null).OrderBy(x => x.Code).ToListAsync(ct);
    public void Add(Location location) => db.Locations.Add(location);
}

internal sealed class RackRepository(WcsDbContext db) : IRackRepository
{
    public Task<Rack?> GetAsync(Guid id, CancellationToken ct) => db.Racks.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(Rack rack) => db.Racks.Add(rack);
    public void Remove(Rack rack) => db.Racks.Remove(rack);
    public Task<Rack?> FindByExternalIdAsync(string externalRackId, CancellationToken ct) => db.Racks.FirstOrDefaultAsync(x => x.ExternalRackId == externalRackId, ct);

    public async Task<IReadOnlyList<Rack>> ListAsync(Guid? locationId, RackStatus? status, int take, CancellationToken ct)
    {
        var q = db.Racks.AsQueryable();
        if (locationId is not null) q = q.Where(x => x.CurrentLocationId == locationId);
        if (status is not null) q = q.Where(x => x.Status == status);
        return await q.OrderBy(x => x.LastConfirmedAt).ThenBy(x => x.Id).Take(take).ToListAsync(ct);
    }

    public Task<int> CountAtLocationAsync(Guid locationId, CancellationToken ct) =>
        db.Racks.CountAsync(x => x.CurrentLocationId == locationId, ct);

    public async Task<IReadOnlyDictionary<Guid, int>> CountByLocationAsync(CancellationToken ct) =>
        await db.Racks.Where(x => x.CurrentLocationId != null).GroupBy(x => x.CurrentLocationId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    private IQueryable<Rack> Unreserved(Guid locationId, RackStatus status) =>
        db.Racks.Where(r => r.CurrentLocationId == locationId && r.Status == status &&
                            !db.Reservations.Any(v => v.RackId == r.Id && v.Status == ReservationStatus.Active))
            .OrderBy(r => r.LastConfirmedAt).ThenBy(r => r.Id);

    // CR-042 (OQ-42-08): a rack that a transport order owns is not offered to anyone else (the owner passes its own id)
    private IQueryable<Rack> Available(Guid locationId, RackStatus status, Guid? orderId) => Unreserved(locationId, status).Where(RackOwnership.NotOwnedByAnother(db, orderId));

    private static Task<Rack?> FirstAsync(IQueryable<Rack> q, RackSelection? selection, CancellationToken ct)
    {
        if (selection is null) return q.FirstOrDefaultAsync(ct);
        if (selection.AutoOnly) q = q.Where(r => r.Disposition == RackDisposition.Auto);
        if (selection.ProductModelId is { } pm) q = q.Where(r => r.ProductModelId == pm);
        else if (selection.RequireNoModel) q = q.Where(r => r.ProductModelId == null);
        return q.OrderBy(r => r.LastConfirmedAt).ThenBy(r => r.Id).FirstOrDefaultAsync(ct);
    }

    public Task<Rack?> FindUnreservedAsync(Guid locationId, RackStatus status, CancellationToken ct) =>
        FirstAsync(Available(locationId, status, null), null, ct);

    public Task<Rack?> FindForOrderAsync(Guid orderId, Guid locationId, RackStatus status, RackSelection? selection, CancellationToken ct) =>
        FirstAsync(Available(locationId, status, orderId), selection, ct);

    public Task<bool> IsRackRecordedAsync(Guid locationId, RackStatus status, bool autoOnly, CancellationToken ct) =>
        Unreserved(locationId, status).AnyAsync(r => !autoOnly || r.Disposition == RackDisposition.Auto, ct);

    public async Task<IReadOnlyList<Rack>> ListUnreservedAsync(Guid locationId, RackStatus status, int take, CancellationToken ct) =>
        await Unreserved(locationId, status).Take(take).ToListAsync(ct);

    // CR-036: the filtered variant (same ordering: FIFO by LastConfirmedAt).
    public Task<Rack?> FindUnreservedAsync(Guid locationId, RackStatus status, RackSelection selection, CancellationToken ct) =>
        FirstAsync(Available(locationId, status, null), selection, ct);
}

internal sealed class ProductModelRepository(WcsDbContext db) : IProductModelRepository
{
    public Task<ProductModel?> GetAsync(Guid id, CancellationToken ct) => db.ProductModels.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<ProductModel?> GetByCodeAsync(string code, CancellationToken ct)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        return db.ProductModels.FirstOrDefaultAsync(x => x.Code == c, ct);
    }
    public async Task<IReadOnlyList<ProductModel>> ListAsync(CancellationToken ct) => await db.ProductModels.OrderBy(x => x.Code).ToListAsync(ct);
    public void Add(ProductModel model) => db.ProductModels.Add(model);
}

internal sealed class ReservationRepository(WcsDbContext db) : IReservationRepository
{
    public void Add(Reservation reservation) => db.Reservations.Add(reservation);

    public async Task RemoveAllForTaskAsync(Guid taskId, CancellationToken ct)
    {
        var rows = await db.Reservations.Where(x => x.TaskId == taskId).ToListAsync(ct);
        db.Reservations.RemoveRange(rows);
        foreach (var pending in db.ChangeTracker.Entries<Reservation>().Where(e => e.State == EntityState.Added && e.Entity.TaskId == taskId).ToList())
            pending.State = EntityState.Detached;
    }

    public async Task<IReadOnlyList<Reservation>> ListActiveByTaskAsync(Guid taskId, CancellationToken ct)
    {
        // Include reservations added in this unit of work that are not yet saved.
        var saved = await db.Reservations.Where(x => x.TaskId == taskId && x.Status == ReservationStatus.Active).ToListAsync(ct);
        var pending = db.ChangeTracker.Entries<Reservation>()
            .Where(e => e.State == EntityState.Added && e.Entity.TaskId == taskId && e.Entity.Status == ReservationStatus.Active)
            .Select(e => e.Entity).Where(e => saved.All(s => s.Id != e.Id));
        return saved.Concat(pending).ToList();
    }

    public Task<bool> HasActiveForRackAsync(Guid rackId, CancellationToken ct) =>
        db.Reservations.AnyAsync(x => x.RackId == rackId && x.Status == ReservationStatus.Active, ct);

    public async Task<IReadOnlyList<Reservation>> ListActiveForRackAsync(Guid rackId, CancellationToken ct) =>
        await db.Reservations.AsNoTracking().Where(x => x.RackId == rackId && x.Status == ReservationStatus.Active).OrderBy(x => x.CreatedAt).ToListAsync(ct);

    public async Task<int> CountActiveDestinationAsync(Guid locationId, CancellationToken ct)
    {
        var saved = await db.Reservations.CountAsync(x => x.LocationId == locationId &&
            x.Kind == ReservationKind.DestinationLocation && x.Status == ReservationStatus.Active, ct);
        // Reservations created earlier in the same unit of work but not yet saved still count.
        var pending = db.ChangeTracker.Entries<Reservation>().Count(e => e.State == EntityState.Added &&
            e.Entity.LocationId == locationId && e.Entity.Kind == ReservationKind.DestinationLocation &&
            e.Entity.Status == ReservationStatus.Active);
        return saved + pending;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountActiveDestinationByLocationAsync(CancellationToken ct) =>
        await db.Reservations.Where(x => x.Kind == ReservationKind.DestinationLocation && x.Status == ReservationStatus.Active)
            .GroupBy(x => x.LocationId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
}

internal sealed class InventoryTransactionRepository(WcsDbContext db) : IInventoryTransactionRepository
{
    public void Add(InventoryTransaction tx) => db.InventoryTransactions.Add(tx);

    public async Task<IReadOnlyList<InventoryTransaction>> ListAsync(Guid? locationId, Guid? taskId, int take, CancellationToken ct)
    {
        var q = db.InventoryTransactions.AsNoTracking().AsQueryable();
        if (locationId is not null) q = q.Where(x => x.FromLocationId == locationId || x.ToLocationId == locationId);
        if (taskId is not null) q = q.Where(x => x.TaskId == taskId);
        return await q.OrderByDescending(x => x.OccurredAt).Take(take).ToListAsync(ct);
    }
}

internal sealed class ProcessedEventRepository(WcsDbContext db) : IProcessedEventRepository
{
    public Task<bool> ExistsAsync(string source, string eventKey, CancellationToken ct) =>
        db.ProcessedEvents.AnyAsync(x => x.Source == source && x.EventKey == eventKey, ct);
    public void Add(ProcessedEvent processed) => db.ProcessedEvents.Add(processed);
    public async Task RemoveForTaskAsync(Guid taskId, CancellationToken ct) =>
        db.ProcessedEvents.RemoveRange(await db.ProcessedEvents.Where(x => x.TaskId == taskId).ToListAsync(ct));
}

internal sealed class TransportTaskRepository(WcsDbContext db) : ITransportTaskRepository
{
    private static readonly TaskState[] Occupying = TaskStateMachine.AgvOccupyingStates.ToArray();

    private IQueryable<TransportTask> WithHistory => db.Tasks.Include(t => t.History);

    public void Add(TransportTask task) => db.Tasks.Add(task);
    public void Remove(TransportTask task) => db.Tasks.Remove(task);
    public Task<TransportTask?> GetAsync(Guid id, CancellationToken ct) => WithHistory.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<TransportTask?> GetByRequestKeyAsync(string requestKey, CancellationToken ct) =>
        WithHistory.FirstOrDefaultAsync(x => x.RequestKey == requestKey, ct);
    public Task<TransportTask?> GetByRcsTaskCodeAsync(string rcsTaskCode, CancellationToken ct) =>
        WithHistory.FirstOrDefaultAsync(x => x.RcsTaskCode == rcsTaskCode, ct);
    public Task<TransportTask?> GetActiveForAgvAsync(Guid agvId, CancellationToken ct) =>
        db.Tasks.FirstOrDefaultAsync(x => x.AgvId == agvId && Occupying.Contains(x.State), ct);
    public Task<bool> AnyQueuedForAgvAsync(Guid agvId, CancellationToken ct) =>
        db.Tasks.AnyAsync(x => x.AgvId == agvId && x.State == TaskState.Queued, ct);
    public Task<TransportTask?> NextQueuedForAgvAsync(Guid agvId, CancellationToken ct) =>
        WithHistory.Where(x => x.AgvId == agvId && x.State == TaskState.Queued).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TransportTask>> ListRequiringReconciliationAsync(int take, CancellationToken ct) =>
        await db.Tasks.AsNoTracking().Where(x => x.RequiresReconciliation).OrderByDescending(x => x.UpdatedAt).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<TransportTask>> ListByStatesAsync(IReadOnlyCollection<TaskState> states, int take, CancellationToken ct)
    {
        var arr = states.ToArray();
        return await db.Tasks.AsNoTracking().Where(x => arr.Contains(x.State)).OrderBy(x => x.CreatedAt).Take(take).ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<TransportTask> Items, int Total)> QueryAsync(TaskQuery query, CancellationToken ct)
    {
        var q = db.Tasks.AsNoTracking().AsQueryable();
        if (query.State is not null) q = q.Where(x => x.State == query.State);
        if (query.AgvId is not null) q = q.Where(x => x.AgvId == query.AgvId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.CreatedAt).Skip(query.Skip).Take(query.Take).ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> ActiveTaskIdByAgvAsync(CancellationToken ct) =>
        await db.Tasks.AsNoTracking().Where(x => x.AgvId != null && Occupying.Contains(x.State) && x.RcsDispatchState == null).ToDictionaryAsync(x => x.AgvId!.Value, x => x.Id, ct);   // CR-028: Legacy tasks only (an RcsManaged task may share a robot)

    public Task<int> CountByStatesAsync(IReadOnlyCollection<TaskState> states, CancellationToken ct)
    {
        var arr = states.ToArray();
        return db.Tasks.CountAsync(x => arr.Contains(x.State), ct);
    }

    private static readonly TaskState[] Finished = [TaskState.Completed, TaskState.Failed, TaskState.Cancelled];

    public Task<int> CountActiveByModeAsync(bool rcsManaged, CancellationToken ct) => rcsManaged
        ? db.Tasks.CountAsync(x => x.RcsDispatchState != null && !Finished.Contains(x.State), ct)
        : db.Tasks.CountAsync(x => x.RcsDispatchState == null && !Finished.Contains(x.State), ct);

    public async Task<IReadOnlyList<TransportTask>> ListActiveRcsManagedAsync(int take, CancellationToken ct) =>
        await WithHistory.Where(x => x.RcsDispatchState != null && !Finished.Contains(x.State)).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(ct);
}
