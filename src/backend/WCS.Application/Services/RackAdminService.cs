using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;

namespace WCS.Application.Services;

public sealed record RackCreateCommand(string LocationCode, RackKind RackKind, string? ExternalRackId, Guid? ProductModelId, string Reason, string? RackType = null);

/// <summary>NULL = unchanged. <see cref="ExternalRackId"/> "" removes the identifier.</summary>
public sealed record RackUpdateCommand(string? LocationCode, RackKind? RackKind, string? ExternalRackId, string Reason, string? RackType = null, Guid? ProductModelId = null);

/// <summary>CR-041: whether a person may correct or remove a rack now, and if not, why (the same guard the change itself runs).</summary>
public sealed record RackEditabilityDto(bool Editable, string? Code, string? Message);

/// <summary>
/// CR-040: create, correct, move and remove ONE rack record. These are corrections of what WCS believes about the physical world, confirmed by a person with a reason; they never move a rack.
/// A rack that is reserved, on an AMR, used by an open task / transport order, in manual handling or in an unknown state is not edited or removed. Every change writes an inventory
/// transaction in the same unit of work; removing a rack record never removes its history.
/// </summary>
public sealed class RackAdminService(IUnitOfWork uow, IRackRepository racks, ILocationRepository locations, IReservationRepository reservations, IRoutingRepository routing,
    IInventoryTransactionRepository transactions, IProductModelRepository models, RackPlacement placement, IWcsNotifier notifier, IClock clock)
{
    public async Task<RackDto> CreateAsync(RackCreateCommand cmd, string user, CancellationToken ct = default)
    {
        var reason = Required(cmd.Reason);
        var dto = await uow.ExecuteAsync(async t =>
        {
            var loc = await UsableLocationAsync(cmd.LocationCode, cmd.RackKind, t);
            await FreeSlotAsync(loc, t);
            var externalId = await UniqueIdAsync(cmd.ExternalRackId, null, t);
            var now = clock.UtcNow;
            var rack = new Rack(StatusOf(cmd.RackKind), loc.Id, RackTypeOf(cmd.RackType), externalId, now);
            if (cmd.ProductModelId is { } mid) await AssignModelAsync(rack, mid, t);
            racks.Add(rack);
            if (placement.OnPlaced(rack, loc, null, ConfirmationSource.Operator, user, now) is { } disposition) transactions.Add(disposition);
            Record(rack.Id, null, loc.Id, null, rack.Status, user, $"Rack {Name(externalId)} created at {loc.Code}: {reason}");
            loc.Touch(now);
            return rack.ToDto();
        }, ct);
        await NotifyAsync(cmd.LocationCode, ct);
        return dto;
    }

    public async Task<RackDto> UpdateAsync(Guid id, RackUpdateCommand cmd, string user, CancellationToken ct = default)
    {
        var reason = Required(cmd.Reason);
        var dto = await uow.ExecuteAsync(async t =>
        {
            var (rack, current) = await EditableAsync(id, t);
            var now = clock.UtcNow;
            var kind = cmd.RackKind ?? (rack.Status == RackStatus.Full ? RackKind.Full : RackKind.Empty);
            var moving = !string.IsNullOrWhiteSpace(cmd.LocationCode) && !string.Equals(cmd.LocationCode.Trim(), current.Code, StringComparison.OrdinalIgnoreCase);
            var target = moving ? await UsableLocationAsync(cmd.LocationCode!, kind, t) : current;
            if (moving) await FreeSlotAsync(target, t);
            else if (!target.Accepts(kind)) throw AppException.Validation("RACK_KIND_NOT_ALLOWED", $"{target.Code} does not accept {kind} racks.");
            var before = rack.Status;
            var changes = new List<string>();
            if (cmd.ExternalRackId is not null)
            {
                var externalId = await UniqueIdAsync(cmd.ExternalRackId, rack.Id, t);
                if (externalId != rack.ExternalRackId) { changes.Add($"id {Name(rack.ExternalRackId)} -> {Name(externalId)}"); rack.CorrectExternalId(externalId); }
            }
            if (cmd.RackType is not null && RackTypeOf(cmd.RackType) is var type && type != rack.RackType) { changes.Add($"type {rack.RackType ?? "(none)"} -> {type ?? "(none)"}"); rack.CorrectRackType(type); }
            if (cmd.ProductModelId is { } model && model != rack.ProductModelId) { await AssignModelAsync(rack, model, t); changes.Add("product model assigned"); }
            if (StatusOf(kind) != before) changes.Add($"{before} -> {StatusOf(kind)}");
            if (moving) changes.Add($"{current.Code} -> {target.Code}");
            if (changes.Count == 0) throw AppException.Rejected("RACK_NOTHING_TO_CHANGE", "Nothing to change.");
            if (moving || StatusOf(kind) != before) rack.PlaceAt(target.Id, StatusOf(kind), now);
            if (moving && placement.OnPlaced(rack, target, null, ConfirmationSource.Operator, user, now) is { } disposition) transactions.Add(disposition);
            Record(rack.Id, current.Id, target.Id, before, rack.Status, user, $"Rack {Name(rack.ExternalRackId)} corrected ({string.Join(", ", changes)}): {reason}");
            current.Touch(now); if (moving) target.Touch(now);
            return rack.ToDto();
        }, ct);
        await NotifyAsync(cmd.LocationCode ?? "", ct);
        return dto;
    }

    public async Task<Guid> DeleteAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var why = Required(reason);
        var code = await uow.ExecuteAsync(async t =>
        {
            var (rack, current) = await EditableAsync(id, t);
            Record(rack.Id, current.Id, null, rack.Status, null, user, $"Rack {Name(rack.ExternalRackId)} removed from {current.Code}: {why}");
            racks.Remove(rack);
            current.Touch(clock.UtcNow);
            return current.Code;
        }, ct);
        await NotifyAsync(code, ct);
        return id;
    }

    public Task<RackEditabilityDto> CheckEditableAsync(Guid id, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        try { await EditableAsync(id, t); return new RackEditabilityDto(true, null, null); }
        catch (AppException e) when (e.Kind != ErrorKind.NotFound) { return new RackEditabilityDto(false, e.Code, e.Message); }
    }, ct);

    private async Task AssignModelAsync(Rack rack, Guid modelId, CancellationToken ct)
    {
        var m = await models.GetAsync(modelId, ct) ?? throw AppException.NotFound("MODEL_NOT_FOUND", "Product model not found.");
        if (!m.Enabled) throw AppException.Rejected("MODEL_DISABLED", $"Product model {m.Code} is disabled.");
        try { rack.AssignModel(modelId); }
        catch (DomainException e) { throw AppException.Conflict(e.Code, e.Message); }
    }

    private static string? RackTypeOf(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= 100 ? value : throw AppException.Validation("RACK_TYPE_TOO_LONG", "A rack type has at most 100 characters.");
    }

    // ------------------------------------------------------------------ guards
    /// <summary>The rack and its location, or the reason why the rack must not be changed.</summary>
    private async Task<(Rack Rack, Location Location)> EditableAsync(Guid id, CancellationToken ct)
    {
        var rack = await racks.GetAsync(id, ct) ?? throw AppException.NotFound("RACK_NOT_FOUND", "Rack not found.");
        if (rack.Status == RackStatus.InTransit || rack.CurrentTaskId is not null) throw AppException.Conflict("RACK_IN_TRANSPORT", "The rack is being transported (or still belongs to a task). It cannot be changed.");
        if (await reservations.HasActiveForRackAsync(id, ct)) throw AppException.Conflict("RACK_RESERVED", "The rack is reserved by an active task or transport order. It cannot be changed.");
        if (await routing.FindOpenOrderUsingRackAsync(id, ct) is { } order) throw AppException.Conflict("RACK_IN_USE_BY_ORDER", $"The rack belongs to the open transport order {order}. Finish or cancel the order first.");
        if (rack.Disposition == RackDisposition.ManualHandling) throw AppException.Conflict("RACK_MANUAL_HANDLING_REQUIRES_APPROVED_ADJUSTMENT", "The rack is in manual handling: changing it needs an approved procedure (P2-OQ-16).");
        if (rack.Status is not (RackStatus.Full or RackStatus.Empty) || rack.CurrentLocationId is not { } locationId) throw AppException.Conflict("RACK_STATE_UNKNOWN", "The physical state of the rack is not confirmed: reconcile it first.");
        var loc = (await locations.ListAsync(ct)).First(l => l.Id == locationId);
        if (loc.ReconciliationRequired) throw AppException.Conflict("LOCATION_RECONCILIATION_REQUIRED", $"{loc.Code} needs reconciliation: no manual change until it is resolved.");
        return (rack, loc);
    }

    private async Task<Location> UsableLocationAsync(string code, RackKind kind, CancellationToken ct)
    {
        var loc = await locations.GetByCodeAsync(code.Trim(), ct) ?? throw AppException.NotFound("LOCATION_NOT_FOUND", $"Location '{code}' not found.");
        if (!loc.Enabled) throw AppException.Conflict("LOCATION_DISABLED", $"{loc.Code} is disabled.");
        if (loc.ReconciliationRequired) throw AppException.Conflict("LOCATION_RECONCILIATION_REQUIRED", $"{loc.Code} needs reconciliation: no manual change until it is resolved.");
        if (!loc.Accepts(kind)) throw AppException.Validation("RACK_KIND_NOT_ALLOWED", $"{loc.Code} does not accept {kind} racks.");
        return loc;
    }

    private async Task FreeSlotAsync(Location loc, CancellationToken ct)
    {
        var occupied = await racks.CountAtLocationAsync(loc.Id, ct);
        var reserved = await reservations.CountActiveDestinationAsync(loc.Id, ct);
        if (occupied + reserved >= loc.RackCapacity) throw AppException.Conflict("CAPACITY_EXCEEDED", $"{loc.Code}: no free slot (capacity {loc.RackCapacity}, {occupied} occupied, {reserved} reserved).");
    }

    private async Task<string?> UniqueIdAsync(string? externalId, Guid? ownId, CancellationToken ct)
    {
        var value = externalId?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > 100) throw AppException.Validation("RACK_ID_TOO_LONG", "A rack id has at most 100 characters.");
        if (await racks.FindByExternalIdAsync(value, ct) is { } other && other.Id != ownId) throw AppException.Conflict("RACK_ID_IN_USE", $"Rack id {value} is already used by another rack.");
        return value;
    }

    private static string Required(string? reason) => string.IsNullOrWhiteSpace(reason) ? throw AppException.Validation("REASON_REQUIRED", "A reason is required.") : reason.Trim();
    private static RackStatus StatusOf(RackKind k) => k == RackKind.Full ? RackStatus.Full : RackStatus.Empty;
    private static string Name(string? externalId) => externalId ?? "(no id)";

    private void Record(Guid rackId, Guid? from, Guid? to, RackStatus? before, RackStatus? after, string user, string note) =>
        transactions.Add(new InventoryTransaction(InventoryEventType.ManualAdjustment, rackId, from, to, null, before, after, ConfirmationSource.Operator, user, BindOutcome.NotRequested,
            note.Length > 1000 ? note[..1000] : note, clock.UtcNow));

    private Task NotifyAsync(string location, CancellationToken ct) => notifier.PublishAsync([new WcsEvent("InventoryChanged", new { location })], ct);
}
