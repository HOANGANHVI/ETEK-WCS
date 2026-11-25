using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Domain.Routing;

namespace WCS.Application.Routing;

/// <summary>
/// What WCS read from the PLC when a request appeared: Request_ID (D1500), Rack_Full (D1510), Rack_Empty (D1512), Destination_Available (D1514) and Rack_Type (D1520).
/// Nothing is dispatched before this snapshot is read and validated.
/// </summary>
public sealed record PlcRequestSnapshot(string PlcId, string? PortId, string RequestId, bool PickupRequest, bool DeliveryRequest,
    int? RackFull, int? RackEmpty, int? DestinationAvailable, int? RackType);

public enum PlcRequestOutcome { Accepted = 1, Duplicate = 2, Rejected = 3, ReconciliationRequired = 4 }

public sealed record PlcRequestResult(PlcRequestOutcome Outcome, string Code, string Message, StationTransactionDto? Transaction, OrderDto? Order);

public interface IPlcRequestService
{
    Task<PlcRequestResult> HandleRequestAsync(PlcRequestSnapshot snapshot, CancellationToken ct = default);
    /// <summary>Restart recovery: transactions that were accepted but never got a Transport Task (crash between the two steps) are started again (idempotent).</summary>
    Task<int> ResumeOrphansAsync(CancellationToken ct = default);
}

/// <summary>
/// The WCS side of step 1 of the 9-step exchange: validate what the PLC shows, record the request durably (Station Transaction), then start the Business Process.
/// It does NOT write M9101 or any other PLC signal (timing of the acknowledgement is an open decision, Q-ORD-1) and never substitutes another position for an invalid one.
/// </summary>
public sealed class PlcRequestService(IUnitOfWork uow, IRoutingRepository routing, ILocationRepository locations, IRackRepository racks, IRouteEngine engine, IMaintenanceGate maintenance,
    IClock clock, IOptions<RouteEngineOptions> options, ModelGuard modelGuard) : IPlcRequestService
{
    public async Task<PlcRequestResult> HandleRequestAsync(PlcRequestSnapshot s, CancellationToken ct = default)
    {
        PlcRequestResult Reject(string code, string msg) => new(PlcRequestOutcome.Rejected, code, msg, null, null);

        if (string.IsNullOrWhiteSpace(s.PlcId)) return Reject("PLC_REQUIRED", "PLC id is required.");
        if (string.IsNullOrWhiteSpace(s.RequestId)) return Reject("REQUEST_ID_MISSING", "Request_ID could not be read; the request is not recorded and will be read again.");
        if (s.RequestId.Length > StationTransaction.MaxRequestIdLength) return Reject("REQUEST_ID_TOO_LONG", $"Request_ID exceeds {StationTransaction.MaxRequestIdLength} characters.");
        if (s.PickupRequest && s.DeliveryRequest) return Reject("CONFLICTING_REQUESTS", "Pickup and Delivery are both requested; neither is processed.");
        if (!s.PickupRequest && !s.DeliveryRequest) return Reject("NO_REQUEST", "No request bit is set.");

        // ---- duplicate Request_ID (never a second transaction or task)
        var dup = await uow.ExecuteAsync(async t =>
        {
            var d = await routing.GetTransactionByRequestIdAsync(s.PlcId, s.RequestId, t);
            if (d is null) return null;
            var order = d.OrderId is { } oid ? await routing.GetOrderAsync(oid, t) : null;
            return new PlcRequestResult(PlcRequestOutcome.Duplicate, "DUPLICATE_REQUEST_ID", "This Request_ID was already processed.", d.ToDto(), order?.ToDto([d]));
        }, ct);
        if (dup is not null) return dup;

        // CR-026 (OQ-1): during Maintenance a NEW request is refused BEFORE anything is recorded (no Station Transaction, no port claimed); the caller may repeat it later.
        // A repeated Request_ID was answered above (idempotent). The locked check in the recording transaction below closes the race with the switch.
        if (await maintenance.IsEnabledAsync(ct)) return Reject(MaintenanceCodes.Code, MaintenanceCodes.Message);

        // ---- resolve the location from the registers (only confirmed codes) and its port
        string locationCode; RackKind? kind = null;
        if (s.PickupRequest)
        {
            var hasFull = s.RackFull is > 0; var hasEmpty = s.RackEmpty is > 0;
            if (hasFull == hasEmpty) return Reject("RACK_POSITION_INVALID", "Exactly one of Rack_Full / Rack_Empty must hold a position.");
            var raw = hasFull ? s.RackFull!.Value : s.RackEmpty!.Value;
            kind = hasFull ? RackKind.Full : RackKind.Empty;
            if (!options.Value.PlcLocationCodes.TryGetValue(raw.ToString(), out var mapped))
                return Reject("UNSUPPORTED_LOCATION_CODE", $"Position code {raw} is not a confirmed code; no substitute position is chosen.");
            locationCode = mapped;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(s.PortId)) return Reject("PORT_REQUIRED", "A Delivery request needs the port it was read from (the destination encoding D1514 is not confirmed).");
            var p = await routing.GetPortAsync(s.PortId, ct);
            if (p?.LocationCode is null) return Reject("PORT_NOT_LINKED", "The port is not linked to a location.");
            locationCode = p.LocationCode;
        }

        var port = await routing.FindPortByLocationAsync(locationCode, ct);
        if (port is null) return Reject("PORT_NOT_FOUND", $"No enabled port serves location {locationCode}.");
        if (!string.Equals(port.PlcId, s.PlcId, StringComparison.Ordinal)) return Reject("PLC_PORT_MISMATCH", $"Location {locationCode} is served by {port.PlcId}, not {s.PlcId}.");
        if (s.PortId is not null && !string.Equals(port.Id, s.PortId, StringComparison.Ordinal)) return Reject("LOCATION_CODE_MISMATCH", $"The reported position {locationCode} belongs to port {port.Id}, not {s.PortId}.");
        if (!await routing.IsPlcEnabledAsync(port.PlcId, ct)) return Reject("PLC_DISABLED", $"PLC {port.PlcId} is disabled.");

        // ---- a port with an active Station Transaction takes no second request (checked before anything else is judged about the new request)
        if (await uow.ExecuteAsync(async t => await routing.GetActiveTransactionForPortAsync(port.Id, t) is not null, ct))
            return Reject("PORT_BUSY", $"Port {port.Id} already has an active Station Transaction.");

        // ---- PLC vs WCS inventory: a disagreement is reconciled, never overwritten and never worked around
        var op = s.PickupRequest ? StationOperation.Pickup : StationOperation.Delivery;
        if (s.PickupRequest)
        {
            var mismatch = await uow.ExecuteAsync(async t =>
            {
                var loc = await locations.GetByCodeAsync(locationCode, t);
                if (loc is null) return true;
                var wanted = kind == RackKind.Full ? RackStatus.Full : RackStatus.Empty;
                // CR-036: with Enforce a rack in manual handling does not count as the rack the PLC reports (the request is reconciled, never served automatically)
                return (modelGuard.Enforcing ? await racks.FindUnreservedAsync(loc.Id, wanted, new RackSelection(null, false, true), t) : await racks.FindUnreservedAsync(loc.Id, wanted, t)) is null;
            }, ct);
            if (mismatch)
            {
                var recon = await uow.ExecuteAsync(async t =>
                {
                    await maintenance.EnsureOpenAsync(t);   // CR-026
                    var loc = await locations.GetByCodeAsync(locationCode, t);
                    loc?.SetReconciliationRequired(true, clock.UtcNow);   // no new task may use the uncertain location (inventory-management.md 4.8)
                    var tx = StationTransaction.Create(port.Id, port.PlcId, locationCode, op, null, null, s.RequestId, clock.UtcNow, kind);
                    tx.Advance(StationTransactionState.Requested, clock.UtcNow);
                    tx.ObserveHandshakeStep(1, clock.UtcNow);
                    tx.RequireReconciliation("RECON_PLC_INVENTORY_MISMATCH", clock.UtcNow);
                    routing.Add(tx);
                    return tx.ToDto();
                }, ct);
                return new PlcRequestResult(PlcRequestOutcome.ReconciliationRequired, "RECON_PLC_INVENTORY_MISMATCH",
                    $"The PLC reports a {kind} rack at {locationCode} but WCS inventory does not; the location is marked for reconciliation and nothing is dispatched.", recon, null);
            }
        }

        // ---- record the request durably (the port is claimed here; the database allows only one active transaction per port)
        StationTransactionDto txDto;
        try
        {
            txDto = await uow.ExecuteAsync(async t =>
            {
                await maintenance.EnsureOpenAsync(t);   // CR-026 (OQ-1): row-locked read in the transaction that records the request
                if (await routing.GetActiveTransactionForPortAsync(port.Id, t) is not null) throw AppException.Conflict("PORT_BUSY", $"Port {port.Id} already has an active Station Transaction.");
                var tx = StationTransaction.Create(port.Id, port.PlcId, locationCode, op, null, null, s.RequestId, clock.UtcNow, kind);
                tx.Advance(StationTransactionState.Requested, clock.UtcNow);
                tx.Advance(StationTransactionState.Accepted, clock.UtcNow);     // WCS recorded the request ("Recorded"; the acknowledgement signal is not written here)
                tx.ObserveHandshakeStep(1, clock.UtcNow);
                routing.Add(tx);
                return tx.ToDto();
            }, ct);
        }
        catch (AppException e) when (e.Code == "PORT_BUSY") { return Reject("PORT_BUSY", e.Message); }
        catch (AppException e) when (e.Code == MaintenanceCodes.Code) { return Reject(e.Code, e.Message); }   // nothing was recorded

        return await StartAsync(txDto, kind, s, ct);
    }

    private async Task<PlcRequestResult> StartAsync(StationTransactionDto tx, RackKind? kind, PlcRequestSnapshot s, CancellationToken ct)
    {
        var type = tx.Operation == nameof(StationOperation.Pickup) ? RequestType.PickupRequest : RequestType.DeliveryRequest;
        try
        {
            var r = await engine.StartAsync(new StartOrderCommand($"{tx.PlcId}:{tx.RequestId}", tx.LocationCode, type, kind, null, "PLC", tx.Id), ct);
            var fresh = (await uow.ExecuteAsync(async t => (await routing.GetTransactionAsync(tx.Id, t))!.ToDto(), ct));
            return new PlcRequestResult(PlcRequestOutcome.Accepted, "ACCEPTED", "Request recorded and Transport Task started.", fresh, r.Order);
        }
        catch (AppException e) when (e.Code == MaintenanceCodes.Code)
        {   // CR-026: Maintenance began after the request was recorded. The transaction is NOT failed: it stays Accepted and ResumeOrphansAsync starts its Order when Maintenance ends.
            return new PlcRequestResult(PlcRequestOutcome.Rejected, e.Code, e.Message, tx, null);
        }
        catch (AppException e)
        {   // the business process could not start (not found / not published / ...): the request is closed so the port is free; nothing was dispatched
            await uow.ExecuteAsync(async t => { (await routing.GetTransactionAsync(tx.Id, t))?.Fail($"START_REFUSED: {e.Code}", clock.UtcNow); return 0; }, ct);
            return new PlcRequestResult(PlcRequestOutcome.Rejected, e.Code, e.Message, null, null);
        }
    }

    public async Task<int> ResumeOrphansAsync(CancellationToken ct = default)
    {
        if (await maintenance.IsEnabledAsync(ct)) return 0;   // CR-026: starting the Order of a recorded request is new work
        var orphans = await uow.ExecuteAsync(async t => (await routing.ListTransactionsAsync(true, null, null, 200, t))
            .Where(x => x.OrderId is null && x.State == StationTransactionState.Accepted && x.RequestId is not null).Select(x => x.ToDto()).ToList(), ct);
        foreach (var o in orphans)
        {
            RackKind? kind = Enum.TryParse<RackKind>(await uow.ExecuteAsync(async t => (await routing.GetTransactionAsync(o.Id, t))!.RackKind?.ToString(), ct), out var k) ? k : null;
            await StartAsync(o, kind, new PlcRequestSnapshot(o.PlcId, o.PortId, o.RequestId!, false, false, null, null, null, null), ct);
        }
        return orphans.Count;
    }
}
