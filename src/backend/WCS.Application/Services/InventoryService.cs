using Microsoft.Extensions.Logging;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Flows;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Domain.Tasks;

namespace WCS.Application.Services;

public sealed class InventoryService(
    IUnitOfWork uow,
    ILocationRepository locations,
    IRackRepository racks,
    IReservationRepository reservations,
    IInventoryTransactionRepository transactions,
    ITransportTaskRepository tasks,
    IWcsNotifier notifier,
    IClock clock,
    ILogger<InventoryService> logger,
    Routing.RackPlacement placement,
    Routing.ModelGuard modelGuard,
    IProductModelRepository productModels) : IInventoryService
{
    private static RackStatus StatusFor(RackKind kind) => kind == RackKind.Full ? RackStatus.Full : RackStatus.Empty;

    // -------------------------------------------------------------------------- reservation

    public async Task<NoSourceResult> ReserveSourceAsync(TransportTask task, FlowRule rule, CancellationToken ct = default)
    {
        var candidates = rule.SourceMode == LocationSelectionMode.Request
            ? new List<IReadOnlyList<string>> { new[] { (await locations.GetAsync(task.RequestLocationId, ct))?.Code ?? "" } }
            : rule.SourceGroups.ToList();

        foreach (var group in candidates)
        foreach (var code in group)
        {
            var loc = await locations.GetByCodeAsync(code, ct);
            if (loc is null || !loc.Enabled || loc.ReconciliationRequired) continue;
            var rack = modelGuard.Enforcing
                ? await racks.FindUnreservedAsync(loc.Id, StatusFor(task.RackKind), new RackSelection(null, false, true), ct)   // CR-036: Disposition = Auto only (no model filter: a legacy task has no model)
                : await racks.FindUnreservedAsync(loc.Id, StatusFor(task.RackKind), ct);
            if (rack is null) continue;

            reservations.Add(Reservation.ForSourceRack(task.Id, rack.Id, loc.Id, clock.UtcNow));
            task.AssignSource(loc.Id, rack.Id, clock.UtcNow);
            task.Note("SOURCE_RESERVED", $"location={loc.Code}", "WCS", clock.UtcNow, HistoryDetail.Of("SOURCE_RESERVED", ("location", loc.Code)));
            return new NoSourceResult(true, loc, rack);
        }
        return new NoSourceResult(false, null, null);
    }

    public async Task<Location?> ReserveDestinationAsync(TransportTask task, FlowRule rule, CancellationToken ct = default)
    {
        IEnumerable<string> codes;
        if (rule.DestinationMode == LocationSelectionMode.Request)
        {
            var station = await locations.GetAsync(task.RequestLocationId, ct);
            codes = station is null ? [] : [station.Code];
        }
        else codes = rule.DestinationGroups.SelectMany(g => g);

        foreach (var code in codes)
        {
            var loc = await locations.GetByCodeAsync(code, ct);
            if (loc is null || !loc.Enabled || !loc.Accepts(task.RackKind)) continue;
            var occupied = await racks.CountAtLocationAsync(loc.Id, ct);
            var reserved = await reservations.CountActiveDestinationAsync(loc.Id, ct);
            if (!loc.HasFreeSlot(occupied, reserved)) continue;

            reservations.Add(Reservation.ForDestination(task.Id, loc.Id, clock.UtcNow));
            loc.Touch(clock.UtcNow); // concurrency token: two tasks cannot take the last slot at once
            task.AssignDestination(loc.Id, clock.UtcNow);
            task.Note("DESTINATION_RESERVED", $"location={loc.Code}", "WCS", clock.UtcNow, HistoryDetail.Of("DESTINATION_RESERVED", ("location", loc.Code)));
            return loc;
        }
        return null;
    }

    public async Task ReleaseReservationsAsync(TransportTask task, CancellationToken ct = default)
    {
        foreach (var r in await reservations.ListActiveByTaskAsync(task.Id, ct)) r.Release(clock.UtcNow);
        if (task.DestinationLocationId is { } dest && await locations.GetAsync(dest, ct) is { } loc) loc.Touch(clock.UtcNow);
    }

    public async Task MarkUncertainAsync(TransportTask task, CancellationToken ct = default)
    {
        // Only when the rack has physically left the source and was not confirmed at the destination.
        if (task.PickConfirmedAt is null || task.DropConfirmedAt is not null) return;
        if (task.RackId is { } rid && await racks.GetAsync(rid, ct) is { } rack) rack.MarkUnknown(clock.UtcNow);
        foreach (var id in new[] { task.SourceLocationId, task.DestinationLocationId }.Where(x => x is not null).Distinct())
            if (await locations.GetAsync(id!.Value, ct) is { } loc) loc.SetReconciliationRequired(true, clock.UtcNow);
        logger.LogWarning("Task {TaskId}: rack {RackId} and locations {SourceLocation}/{DestinationLocation} marked uncertain (picked, delivery not confirmed). {EventCode}",
            task.Id, task.RackId, task.SourceLocationId, task.DestinationLocationId, "INVENTORY_UNCERTAIN");   // CR-021
    }

    public async Task MarkPhysicalUnprovenAsync(TransportTask task, CancellationToken ct = default)
    {
        if (task.DropConfirmedAt is not null) return;      // delivered and confirmed: nothing is uncertain
        if (task.RackId is { } rid && await racks.GetAsync(rid, ct) is { } rack && rack.Status != RackStatus.Unknown) rack.MarkUnknown(clock.UtcNow);
        foreach (var id in new[] { task.SourceLocationId, task.DestinationLocationId }.Where(x => x is not null).Distinct())
            if (await locations.GetAsync(id!.Value, ct) is { } loc) loc.SetReconciliationRequired(true, clock.UtcNow);
        logger.LogWarning("Task {TaskId}: rack {RackId} and locations {SourceLocation}/{DestinationLocation} marked uncertain (outcome reported by RCS cannot be proven). {EventCode}",
            task.Id, task.RackId, task.SourceLocationId, task.DestinationLocationId, "INVENTORY_UNCERTAIN");   // CR-021
    }

    // -------------------------------------------------------------------------- confirmed events

    public async Task ConfirmPickAsync(TransportTask task, ConfirmationSource source, string? user, CancellationToken ct = default)
    {
        var rack = await racks.GetAsync(task.RackId ?? Guid.Empty, ct)
                   ?? throw AppException.Conflict("RACK_NOT_FOUND", "The reserved rack no longer exists.");
        if (rack.CurrentLocationId != task.SourceLocationId)
            throw AppException.Conflict("RACK_NOT_AT_SOURCE", "WCS inventory shows the rack is not at the source location.");

        var before = rack.Status;
        rack.BeginTransit(task.Id, clock.UtcNow);
        if (await locations.GetAsync(task.SourceLocationId!.Value, ct) is { } src) src.Touch(clock.UtcNow);
        transactions.Add(new InventoryTransaction(InventoryEventType.PickConfirmed, rack.Id, task.SourceLocationId, null, task.Id,
            before, rack.Status, source, user, BindOutcome.NotRequested, null, clock.UtcNow));
        // The source-rack reservation has served its purpose: the rack is now tracked by the task (IN_TRANSIT).
        foreach (var r in (await reservations.ListActiveByTaskAsync(task.Id, ct)).Where(r => r.Kind == ReservationKind.SourceRack))
            r.Consume(clock.UtcNow);
    }

    public async Task<BindOutcome> ConfirmDropAsync(TransportTask task, FlowRule rule, ConfirmationSource source, string? user,
        string? externalRackId, CancellationToken ct = default)
    {
        var rack = await racks.GetAsync(task.RackId ?? Guid.Empty, ct)
                   ?? throw AppException.Conflict("RACK_NOT_FOUND", "The task rack no longer exists.");
        // CR-008B-5: after "RCS ended" the rack is Unknown but still belongs to this task; a PLC drop confirmation is physical evidence and is accepted
        if (rack.CurrentTaskId != task.Id || rack.Status is not (RackStatus.InTransit or RackStatus.Unknown))
            throw AppException.Conflict("RACK_NOT_IN_TRANSIT", "Drop cannot be confirmed: the rack is not in transit for this task.");
        var dest = await locations.GetAsync(task.DestinationLocationId ?? Guid.Empty, ct)
                   ?? throw AppException.Conflict("DESTINATION_NOT_ASSIGNED", "The task has no destination.");

        // "Không bind rack" points keep the process behaviour: never bind there (inventory-management.md 4.6).
        var bind = BindOutcome.NotRequested;
        if (!string.IsNullOrWhiteSpace(externalRackId))
        {
            if (rule.IsNoBind(dest.Code)) bind = BindOutcome.NotBoundByProcess;
            else { rack.Bind(externalRackId); bind = BindOutcome.Bound; }
        }

        var before = rack.Status;
        rack.PlaceAt(dest.Id, StatusFor(task.RackKind), clock.UtcNow);
        dest.Touch(clock.UtcNow);
        if (placement.OnPlaced(rack, dest, task.Id, source, user, clock.UtcNow) is { } dispositionTx) transactions.Add(dispositionTx);   // CR-036 (no-op unless the destination is a manual QC station)
        transactions.Add(new InventoryTransaction(InventoryEventType.DropConfirmed, rack.Id, task.SourceLocationId, dest.Id, task.Id,
            before, rack.Status, source, user, bind,
            bind == BindOutcome.NotBoundByProcess ? "NO_BIND_POINT: external rack id ignored" : null, clock.UtcNow));
        foreach (var r in await reservations.ListActiveByTaskAsync(task.Id, ct)) r.Consume(clock.UtcNow);
        return bind;
    }

    // -------------------------------------------------------------------------- API-level operations

    public Task<IReadOnlyList<Guid>> DeclareInitialStockAsync(string locationCode, RackKind kind, int quantity, string user,
        CancellationToken ct = default, Guid? productModelId = null)
    {
        if (quantity <= 0) throw AppException.Validation("INVALID_QUANTITY", "Quantity must be positive.");
        return Run(async () =>
        {
            var loc = await RequireLocation(locationCode, ct);
            if (!loc.Accepts(kind)) throw AppException.Validation("RACK_KIND_NOT_ALLOWED", $"{loc.Code} does not accept {kind} racks.");
            var occupied = await racks.CountAtLocationAsync(loc.Id, ct);
            var reserved = await reservations.CountActiveDestinationAsync(loc.Id, ct);
            if (occupied + reserved + quantity > loc.RackCapacity)
                throw AppException.Conflict("CAPACITY_EXCEEDED", $"{loc.Code}: occupied {occupied} + reserved {reserved} + {quantity} exceeds capacity {loc.RackCapacity}.");

            var modelId = await ResolveStockModelAsync(loc, productModelId, ct);   // CR-036
            var ids = new List<Guid>();
            for (var i = 0; i < quantity; i++)
            {
                var rack = new Rack(StatusFor(kind), loc.Id, null, null, clock.UtcNow);
                racks.Add(rack);
                if (modelId is { } mid) { rack.AssignModel(mid); transactions.Add(new InventoryTransaction(InventoryEventType.ModelAssigned, rack.Id, null, loc.Id, null, null, rack.Status, ConfirmationSource.Operator, user, BindOutcome.NotRequested, "Product model declared at stock-in", clock.UtcNow)); }
                if (placement.OnPlaced(rack, loc, null, ConfirmationSource.Operator, user, clock.UtcNow) is { } dispositionTx) transactions.Add(dispositionTx);
                transactions.Add(new InventoryTransaction(InventoryEventType.InitialStockDeclared, rack.Id, null, loc.Id, null,
                    null, rack.Status, ConfirmationSource.Operator, user, BindOutcome.NotRequested, "Initial stock declared", clock.UtcNow));
                ids.Add(rack.Id);
            }
            loc.Touch(clock.UtcNow);
            return (IReadOnlyList<Guid>)ids;
        }, locationCode);
    }

    public Task<int> AdjustStockAsync(string locationCode, RackKind kind, int targetQuantity, string user, string? note,
        CancellationToken ct = default, Guid? productModelId = null)
    {
        if (targetQuantity < 0) throw AppException.Validation("INVALID_QUANTITY", "Target quantity cannot be negative.");
        return Run(async () =>
        {
            var loc = await RequireLocation(locationCode, ct);
            if (!loc.Accepts(kind)) throw AppException.Validation("RACK_KIND_NOT_ALLOWED", $"{loc.Code} does not accept {kind} racks.");
            var status = StatusFor(kind);
            var all = await racks.ListAsync(loc.Id, status, 10_000, ct);
            var current = all.Count;
            var reserved = await reservations.CountActiveDestinationAsync(loc.Id, ct);
            var occupiedTotal = await racks.CountAtLocationAsync(loc.Id, ct);
            var delta = targetQuantity - current;

            if (delta > 0)
            {
                if (occupiedTotal + reserved + delta > loc.RackCapacity)
                    throw AppException.Conflict("CAPACITY_EXCEEDED", $"{loc.Code}: adjustment exceeds capacity {loc.RackCapacity}.");
                var modelId = await ResolveStockModelAsync(loc, productModelId, ct);   // CR-036
                for (var i = 0; i < delta; i++)
                {
                    var rack = new Rack(status, loc.Id, null, null, clock.UtcNow);
                    racks.Add(rack);
                    if (modelId is { } mid) { rack.AssignModel(mid); transactions.Add(new InventoryTransaction(InventoryEventType.ModelAssigned, rack.Id, null, loc.Id, null, null, rack.Status, ConfirmationSource.Operator, user, BindOutcome.NotRequested, "Product model declared at stock adjustment", clock.UtcNow)); }
                    if (placement.OnPlaced(rack, loc, null, ConfirmationSource.Operator, user, clock.UtcNow) is { } dispositionTx) transactions.Add(dispositionTx);
                    transactions.Add(new InventoryTransaction(InventoryEventType.ManualAdjustment, rack.Id, null, loc.Id, null, null,
                        status, ConfirmationSource.Operator, user, BindOutcome.NotRequested, note ?? "Manual adjustment (+)", clock.UtcNow));
                }
            }
            else if (delta < 0)
            {
                var removable = await racks.ListUnreservedAsync(loc.Id, status, -delta, ct);
                if (modelGuard.On)
                {
                    // CR-036 (A.1): a rack in manual handling is NOT removed like an Auto rack. Removing one needs an approved, audited procedure that does not exist yet (open question P2-OQ-16),
                    // so the request fails closed and nothing changes (no rack removed, no disposition touched).
                    var unreserved = await racks.ListUnreservedAsync(loc.Id, status, 10_000, ct);
                    var autoRacks = unreserved.Where(r => r.Disposition == RackDisposition.Auto).ToList();
                    var manualRacks = unreserved.Count - autoRacks.Count;
                    if (autoRacks.Count < -delta && autoRacks.Count + manualRacks >= -delta)
                        throw AppException.Conflict("RACK_MANUAL_HANDLING_REQUIRES_APPROVED_ADJUSTMENT",
                            $"{loc.Code} holds {manualRacks} rack(s) in manual handling. They are not removed by a stock adjustment: removing them needs an approved procedure (P2-OQ-16). Nothing was changed.");
                    removable = autoRacks.Take(-delta).ToList();
                }
                if (removable.Count < -delta)
                    throw AppException.Conflict("RACKS_RESERVED", "Cannot remove racks that are reserved by active tasks.");
                foreach (var rack in removable)
                {
                    transactions.Add(new InventoryTransaction(InventoryEventType.ManualAdjustment, rack.Id, loc.Id, null, null, status,
                        null, ConfirmationSource.Operator, user, BindOutcome.NotRequested, note ?? "Manual adjustment (-)", clock.UtcNow));
                    racks.Remove(rack);
                }
            }
            loc.Touch(clock.UtcNow);
            return targetQuantity;
        }, locationCode);
    }

    public async Task ReconcileTaskAsync(Guid taskId, string actualLocationCode, string user, string? note, CancellationToken ct = default)
    {
        await uow.ExecuteAsync<bool>(async token =>
        {
            string? modelIssue = null;   // CR-036 (A.1)
            var task = await tasks.GetAsync(taskId, token) ?? throw AppException.NotFound("TASK_NOT_FOUND", "Task not found.");
            if (!task.IsTerminal) throw AppException.Rejected("TASK_NOT_FINISHED", "Only finished tasks can be reconciled.");
            if (!task.RequiresReconciliation) throw AppException.Rejected("NO_RECONCILIATION_NEEDED", "Task does not require reconciliation.");
            var actual = await RequireLocation(actualLocationCode, token);

            if (task.RackId is { } rid && await racks.GetAsync(rid, token) is { } rack)
            {
                if (!actual.Accepts(task.RackKind)) throw AppException.Validation("RACK_KIND_NOT_ALLOWED", $"{actual.Code} does not accept {task.RackKind} racks.");
                var occupied = await racks.CountAtLocationAsync(actual.Id, token);
                var ownReservation = (await reservations.ListActiveByTaskAsync(task.Id, token))
                    .Count(r => r.Kind == ReservationKind.DestinationLocation && r.LocationId == actual.Id);
                var reserved = await reservations.CountActiveDestinationAsync(actual.Id, token) - ownReservation;
                if (rack.CurrentLocationId != actual.Id && occupied + reserved >= actual.RackCapacity)
                    throw AppException.Conflict("CAPACITY_EXCEEDED", $"{actual.Code} has no free slot for the reconciled rack.");

                var before = rack.Status;
                rack.PlaceAt(actual.Id, StatusFor(task.RackKind), clock.UtcNow);
                if (placement.OnPlaced(rack, actual, task.Id, ConfirmationSource.WcsReconciliation, user, clock.UtcNow) is { } dispositionTx) transactions.Add(dispositionTx);   // CR-036
                transactions.Add(new InventoryTransaction(InventoryEventType.Reconciliation, rack.Id, task.SourceLocationId, actual.Id, task.Id,
                    before, rack.Status, ConfirmationSource.WcsReconciliation, user, BindOutcome.NotRequested, note, clock.UtcNow));
                // CR-036 (A.1): the physical position is recorded as the operator confirmed it. The rack model is NEVER changed to fit the location and the confirmation is NEVER refused:
                // when rack and location disagree about the model the location is flagged afterwards (so nothing is dispatched from or to it automatically) and the incident is recorded.
                if (modelGuard.On)
                {
                    modelIssue = rack.ProductModelId is null ? (actual.ProductModelId is null ? null : Routing.ModelCodes.RackModelUnknown)
                        : (rack.ProductModelId == actual.ProductModelId ? null : Routing.ModelCodes.ModelMismatch);
                    if (modelIssue is not null)
                    {
                        task.Note("MODEL_GUARD", $"{modelGuard.Mode}: {modelIssue} - the reconciled rack (model {rack.ProductModelId?.ToString() ?? "unknown"}) was confirmed at {actual.Code} (model {actual.ProductModelId?.ToString() ?? "none"})", "WCS", clock.UtcNow,
                            HistoryDetail.Of("MODEL_GUARD", ("code", modelIssue), ("location", actual.Code)));
                        logger.LogWarning("Reconciliation of task {TaskId}: {Code} at {Location} (ModelIsolation {Mode}). {EventCode}", taskId, modelIssue, actual.Code, modelGuard.Mode, "MODEL_GUARD_RECONCILIATION");
                    }
                }
            }

            foreach (var r in await reservations.ListActiveByTaskAsync(task.Id, token)) r.Release(clock.UtcNow);
            foreach (var id in new[] { task.SourceLocationId, task.DestinationLocationId, actual.Id }.Where(x => x is not null).Distinct())
                if (await locations.GetAsync(id!.Value, token) is { } l) l.SetReconciliationRequired(false, clock.UtcNow);
            if (modelIssue is not null && modelGuard.Enforcing) actual.SetReconciliationRequired(true, clock.UtcNow);   // CR-036: after the clean-up above, so it stays set
            task.MarkReconciled(clock.UtcNow);
            task.Note("RECONCILED", $"actual={actual.Code}; by={user}; {note}", "Operator", clock.UtcNow, HistoryDetail.Of("RECONCILED", ("actual", actual.Code), ("by", user), ("note", note)));
            return true;
        }, ct);
        await notifier.PublishAsync([new WcsEvent("InventoryChanged", new { taskId, reason = "reconciled" })], ct);
        logger.LogInformation("Task {TaskId} reconciled by {User} at {Location}. {EventCode}", taskId, user, actualLocationCode, "INVENTORY_RECONCILED");
    }

    // -------------------------------------------------------------------------- helpers

    private async Task<Location> RequireLocation(string code, CancellationToken ct) =>
        await locations.GetByCodeAsync(code, ct) ?? throw AppException.NotFound("LOCATION_NOT_FOUND", $"Location '{code}' not found.");

    /// <summary>CR-036 (P2-OQ-04, option A): the model of a new rack is declared by the person who enters the stock. Never inferred from the location.</summary>
    private async Task<Guid?> ResolveStockModelAsync(Location loc, Guid? requested, CancellationToken ct)
    {
        if (requested is { } id)
        {
            var m = await productModels.GetAsync(id, ct) ?? throw AppException.NotFound("MODEL_NOT_FOUND", "Product model not found.");
            if (!m.Enabled) throw AppException.Rejected("MODEL_DISABLED", $"Product model {m.Code} is disabled.");
            if (modelGuard.On && loc.ProductModelId is { } lm && lm != id)
            {
                if (modelGuard.Enforcing) throw AppException.Conflict("RACK_MODEL_LOCATION_MISMATCH", $"{loc.Code} belongs to another product model.");
                logger.LogWarning("Stock of model {Model} entered at {Location}, which belongs to another model (ModelIsolation Warn). {EventCode}", m.Code, loc.Code, "MODEL_GUARD_WARN");
            }
            return id;
        }
        if (modelGuard.Enforcing && loc.ProductModelId is not null)
            throw AppException.Validation("RACK_MODEL_REQUIRED", $"{loc.Code} belongs to a product model: the model of the new racks must be given.");
        return null;
    }

    private async Task<T> Run<T>(Func<Task<T>> work, string locationCode)
    {
        var result = await uow.ExecuteAsync(_ => work());
        await notifier.PublishAsync([new WcsEvent("InventoryChanged", new { location = locationCode })]);
        return result;
    }
}
