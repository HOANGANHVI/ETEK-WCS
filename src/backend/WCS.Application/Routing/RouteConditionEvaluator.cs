using WCS.Application.Abstractions;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Domain.Routing;

namespace WCS.Application.Routing;

/// <summary>
/// Evaluates the controlled condition types against WCS inventory and the PLC link state. It never executes configuration text: a condition is a
/// <see cref="ConditionType"/> plus a parameter, and an unknown parameter makes the condition FALSE (never "unknown means true").
/// </summary>
public sealed class RouteConditionEvaluator(ILocationRepository locations, IRackRepository racks, IReservationRepository reservations,
    IRoutingRepository routing, IPlcLinkStatus link, ModelGuard modelGuard)
{
    private static RackStatus StatusOf(RackKind k) => k == RackKind.Full ? RackStatus.Full : RackStatus.Empty;

    private static bool StoresRacks(LocationType t) => t is not (LocationType.WaitingPosition or LocationType.Waypoint);

    /// <summary>True for a location type that can hold a stored rack (an intermediate waiting position never does).</summary>
    public static bool CanStoreRack(LocationType t) => StoresRacks(t);

    public async Task<bool> EvaluateAsync(ConditionSpec c, string defaultLocationCode, RackKind kind, CancellationToken ct = default, RackSelection? selection = null)
    {
        var code = string.IsNullOrWhiteSpace(c.LocationCode) ? defaultLocationCode : c.LocationCode!;
        var loc = await locations.GetByCodeAsync(code, ct);
        if (loc is null) return false;
        switch (c.Type)
        {
            case ConditionType.LocationEnabled:
                return loc.Enabled;
            case ConditionType.LocationAvailable:
            case ConditionType.CapacityAvailable:
            {
                if (c.Type == ConditionType.LocationAvailable && !loc.Accepts(kind)) return false;
                var (occ, res) = await CountsAsync(loc, ct);
                return StoresRacks(loc.Type) && loc.HasFreeSlot(occ, res);
            }
            case ConditionType.RackPresent:
                return loc.Enabled && !loc.ReconciliationRequired && await FindRackAsync(loc.Id, kind, selection, ct) is not null;
            case ConditionType.RackTypeMatch:
            {
                var rack = await FindRackAsync(loc.Id, kind, selection, ct);
                if (rack is null || string.IsNullOrWhiteSpace(c.Parameter)) return false;
                if (Enum.TryParse<RackKind>(c.Parameter, true, out var wanted)) return wanted == kind;
                return string.Equals(rack.RackType, c.Parameter, StringComparison.OrdinalIgnoreCase);
            }
            case ConditionType.StationReady:
                return await PortReadyAsync(code, null, ct);
            case ConditionType.PlcOnline:
                return await PlcOnlineAsync(code, ct);
            case ConditionType.InventoryState:
            {
                var (occ, res) = await CountsAsync(loc, ct);
                return (c.Parameter ?? "").Trim().ToLowerInvariant() switch
                {
                    "empty" => !loc.ReconciliationRequired && occ == 0,
                    "occupied" => occ > 0,
                    "hasfreeslot" => loc.HasFreeSlot(occ, res),
                    "notunknown" => !loc.ReconciliationRequired,
                    _ => false,
                };
            }
            case ConditionType.RackBinding:
            {
                var rack = await FindRackAsync(loc.Id, kind, selection, ct);
                if (rack is null) return false;
                return (c.Parameter ?? "").Trim().ToLowerInvariant() switch
                {
                    "bound" => rack.ExternalRackId is not null,
                    "unbound" => rack.ExternalRackId is null,
                    _ => false,
                };
            }
            default:
                return false;
        }
    }

    // CR-036: with a selection (Enforce) the model / manual-handling filter applies; without it the historical query is used.
    private Task<Rack?> FindRackAsync(Guid locationId, RackKind kind, RackSelection? selection, CancellationToken ct) =>
        selection is null ? racks.FindUnreservedAsync(locationId, StatusOf(kind), ct) : racks.FindUnreservedAsync(locationId, StatusOf(kind), selection, ct);

    /// <summary>All conditions must hold.</summary>
    public async Task<bool> AllAsync(IEnumerable<ConditionSpec> conditions, string defaultLocationCode, RackKind kind, CancellationToken ct = default, RackSelection? selection = null)
    {
        foreach (var c in conditions)
            if (!await EvaluateAsync(c, defaultLocationCode, kind, ct, selection)) return false;
        return true;
    }

    /// <summary>Always-on rules for a SOURCE candidate: usable location, an unreserved rack of the kind, port ready and PLC online.</summary>
    public async Task<bool> SourceUsableAsync(string code, RackKind kind, IEnumerable<ConditionSpec> extra, Guid? orderId, CancellationToken ct = default, ModelContext? mc = null)
    {
        // CR-036: model / manual-QC guards (INV-M1, INV-M2, INV-M3, INV-M4 at run time). mc == null (legacy callers) or Mode Off = the historical behaviour.
        var sel = mc is null ? null : modelGuard.SelectionFor(mc.ProcessModelId);
        if (mc is not null && modelGuard.On && await locations.GetByCodeAsync(code, ct) is { } l)
        {
            var v = ModelGuard.LocationViolation(l, mc.ProcessModelId, LocationUse.Source);
            if (v is not null) { modelGuard.Report(v, $"source location {code}"); if (modelGuard.Enforcing) return false; }
        }
        var ok = await EvaluateAsync(new ConditionSpec(ConditionType.LocationEnabled), code, kind, ct)
                 && await EvaluateAsync(new ConditionSpec(ConditionType.RackPresent), code, kind, ct, sel)
                 && await PortReadyAsync(code, orderId, ct) && await PlcOnlineAsync(code, ct)
                 && await AllAsync(extra, code, kind, ct, sel);
        // say WHY (Enforce: a rack was excluded; Warn: a rack would be excluded) - the rack query without the filter is only used to explain
        if (mc is not null && modelGuard.On && (sel is not null ? !ok : ok) && await locations.GetByCodeAsync(code, ct) is { } loc
            && await racks.FindUnreservedAsync(loc.Id, StatusOf(kind), ct) is { } any && ModelGuard.RackViolation(any, mc.ProcessModelId) is { } rv)
            modelGuard.Report(rv, $"rack at {code}");
        return ok;
    }

    /// <summary>Always-on rules for a DESTINATION candidate: enabled, accepts the rack kind, free slot, port ready and PLC online.</summary>
    public async Task<bool> DestinationUsableAsync(string code, RackKind kind, IEnumerable<ConditionSpec> extra, Guid? orderId, CancellationToken ct = default, ModelContext? mc = null)
    {
        if (mc is not null && modelGuard.On && await locations.GetByCodeAsync(code, ct) is { } l)
        {
            var v = ModelGuard.LocationViolation(l, mc.ProcessModelId, LocationUse.Destination);
            if (v is not null) { modelGuard.Report(v, $"destination location {code}"); if (modelGuard.Enforcing) return false; }
        }
        return await DestinationChecksAsync(code, kind, extra, orderId, ct);
    }

    private async Task<bool> DestinationChecksAsync(string code, RackKind kind, IEnumerable<ConditionSpec> extra, Guid? orderId, CancellationToken ct) =>
        await EvaluateAsync(new ConditionSpec(ConditionType.LocationEnabled), code, kind, ct)
        && await EvaluateAsync(new ConditionSpec(ConditionType.LocationAvailable), code, kind, ct)
        && await PortReadyAsync(code, orderId, ct) && await PlcOnlineAsync(code, ct)
        && await AllAsync(extra, code, kind, ct);

    /// <param name="orderId">A transaction that already belongs to this order (for example the one opened by the PLC request) does not make the port busy for the order itself.</param>
    public async Task<bool> PortReadyAsync(string locationCode, Guid? orderId, CancellationToken ct)
    {
        var port = await routing.FindPortByLocationAsync(locationCode, ct);
        if (port is null) return true;                               // a location without a PLC port needs no handshake
        if (!port.Enabled) return false;
        var active = await routing.GetActiveTransactionForPortAsync(port.Id, ct);
        return active is null || (orderId is not null && active.OrderId == orderId);
    }

    public async Task<bool> PlcOnlineAsync(string locationCode, CancellationToken ct)
    {
        var port = await routing.FindPortByLocationAsync(locationCode, ct);
        if (port is null) return true;
        return await routing.IsPlcEnabledAsync(port.PlcId, ct) && link.IsOnline(port.PlcId) == true;   // unknown is treated as offline (safe)
    }

    private async Task<(int Occupied, int Reserved)> CountsAsync(Location loc, CancellationToken ct) =>
        (await racks.CountAtLocationAsync(loc.Id, ct), await reservations.CountActiveDestinationAsync(loc.Id, ct));
}
