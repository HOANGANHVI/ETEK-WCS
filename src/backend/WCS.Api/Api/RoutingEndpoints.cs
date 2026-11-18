using System.ComponentModel.DataAnnotations;
using WCS.Application.Auth;
using WCS.Application.Common;
using WCS.Application.Plc;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Routing;

namespace WCS.Api.Api;

// ---- request models (DataAnnotations are validated by ValidationFilter)
public sealed record StartOrderRequest([property: Required, StringLength(200)] string RequestKey, [property: Required, StringLength(50)] string LocationCode,
    [property: Required] string RequestType, string? RackKind, string? ProcessCode);
public sealed record CancelOrderRequest([property: StringLength(500)] string? Reason);
public sealed record ConfirmDropRequest(string? ExternalRackId);
public sealed record TransactionAdvanceRequest([property: Required] string State);
public sealed record TransactionObserveRequest([property: Range(1, 9)] int Step);
public sealed record TransactionResolveRequest(string? ResumeState);
public sealed record PlcRequestInput([property: Required] string PlcId, string? PortId, [property: Required, StringLength(254)] string RequestId, bool PickupRequest, bool DeliveryRequest,
    int? RackFull, int? RackEmpty, int? DestinationAvailable, int? RackType);
public sealed record ProcessHeaderRequest([property: Required, StringLength(60)] string Code, [property: Required, StringLength(200)] string Name, string? Description,
    [property: Required] string RequestType, [property: Required] string RackKind, string[]? TriggerLocationCodes, bool? Enabled);
public sealed record NewVersionRequest(Guid? FromVersionId);
public sealed record AddPlcRequest([property: Required, StringLength(20)] string PlcId, [property: Required, StringLength(100)] string Name, [property: Required, StringLength(64)] string IpAddress,
    AddPlcStationCommand[]? Stations);
public sealed record AddPortRequest([property: Required, StringLength(20)] string StationId, string? LocationCode);
public sealed record PortLinkRequest(string? LocationCode, bool Enabled);

/// <summary>
/// CR-010 API. Monitoring (orders, transactions) is open to Admin and Operator; every configuration, every physical-event input and every PLC provisioning call is Admin-only
/// and audited. Nothing here writes a PLC signal.
/// </summary>
public static class RoutingEndpoints
{
    public static void MapRoutingApi(this RouteGroupBuilder api)
    {
        var admin = AuthSetup.AdminPolicy;

        // ================================================================ Transport Task (order) monitoring
        var orders = api.MapGroup("/routing").WithTags("Routing");
        orders.MapGet("orders", async (HttpContext c, WCS.Application.Abstractions.IUnitOfWork uow, IRoutingRepository repo, string? state, int? skip, int? take, CancellationToken ct) =>
        {
            OrderState? st = Enum.TryParse<OrderState>(state, true, out var s) ? s : null;
            var (items, total) = await uow.ExecuteAsync(t => repo.QueryOrdersAsync(st, skip ?? 0, Math.Clamp(take ?? 50, 1, 200), t), ct);
            return ApiResults.Ok(c, new { total, items = items.Select(o => o.ToDto()).ToList() });
        });
        orders.MapGet("orders/{id:guid}", async (HttpContext c, Guid id, WCS.Application.Abstractions.IUnitOfWork uow, IRoutingRepository repo, CancellationToken ct) =>
            ApiResults.Ok(c, await uow.ExecuteAsync(async t =>
            {
                var o = await repo.GetOrderAsync(id, t) ?? throw AppException.NotFound("ORDER_NOT_FOUND", "Transport Task not found.");
                return new { order = o.ToDto(await repo.ListTransactionsAsync(false, null, id, 100, t)), events = (await repo.ListEventsAsync(id, 500, t)).Select(e => e.ToDto()).ToList() };
            }, ct)));
        // the processes a user may start: published, enabled and not review-blocked (no configuration detail)
        orders.MapGet("startable-processes", async (HttpContext c, ProcessAdminService s, CancellationToken ct) =>
            ApiResults.Ok(c, (await s.ListAsync(ct)).Where(p => p.PublishedVersion is not null && p.Enabled && !p.ReviewBlocked)
                .Select(p => new { p.Code, p.Name, p.RequestType, p.RackKind, p.TriggerLocationCodes }).ToList()));
        orders.MapPost("orders", async (HttpContext c, StartOrderRequest r, IRouteEngine engine, CancellationToken ct) =>
        {
            var type = DefinitionMapping.ParseRequestType(r.RequestType);
            RackKind? kind = string.IsNullOrWhiteSpace(r.RackKind) ? null : DefinitionMapping.ParseRackKind(r.RackKind);
            var res = await Audited.RunAsync(c, "ROUTING_ORDER_START", "TransportOrder", r.RequestKey, null,
                () => engine.StartAsync(new StartOrderCommand(r.RequestKey, r.LocationCode, type, kind, r.ProcessCode, c.Actor().Username), ct), x => new { x.Order.Id, x.Order.ProcessCode, x.Created });
            return ApiResults.Ok(c, res, res.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        });
        orders.MapPost("orders/{id:guid}/cancel", async (HttpContext c, Guid id, CancelOrderRequest r, IRouteEngine engine, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "ROUTING_ORDER_CANCEL", "TransportOrder", id.ToString(), null,
                () => engine.CancelAsync(id, r.Reason ?? "Cancelled by operator", c.Actor().Username, ct), x => new { x.State }))).RequireAuthorization(admin);

        // physical-event inputs (simulator / AGV system / operator confirmation). Admin only: they move inventory.
        orders.MapPost("legs/{id:guid}/pick", async (HttpContext c, Guid id, IRouteEngine engine, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "ROUTING_LEG_PICK", "TransportLeg", id.ToString(), null,
                () => engine.ConfirmPickAsync(id, ConfirmationSource.Operator, c.Actor().Username, null, ct), x => new { x.Outcome }))).RequireAuthorization(admin);
        orders.MapPost("legs/{id:guid}/drop", async (HttpContext c, Guid id, ConfirmDropRequest r, IRouteEngine engine, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "ROUTING_LEG_DROP", "TransportLeg", id.ToString(), null,
                () => engine.ConfirmDropAsync(id, ConfirmationSource.Operator, c.Actor().Username, r.ExternalRackId, ct), x => new { x.Outcome }))).RequireAuthorization(admin);

        // ================================================================ Station Transaction monitoring and handling
        orders.MapGet("transactions", async (HttpContext c, StationTransactionService s, bool? activeOnly, string? plcId, Guid? orderId, int? take, CancellationToken ct) =>
            ApiResults.Ok(c, await s.ListAsync(activeOnly ?? false, plcId, orderId, Math.Clamp(take ?? 100, 1, 500), ct)));
        orders.MapPost("transactions/{id:guid}/advance", async (HttpContext c, Guid id, TransactionAdvanceRequest r, StationTransactionService s, CancellationToken ct) =>
        {
            if (!Enum.TryParse<StationTransactionState>(r.State, true, out var next)) throw AppException.Validation("STATE_INVALID", "Unknown state.");
            return ApiResults.Ok(c, await Audited.RunAsync(c, "ROUTING_TX_ADVANCE", "StationTransaction", id.ToString(), null, () => s.AdvanceAsync(id, next, ct), x => new { x.State }));
        }).RequireAuthorization(admin);
        orders.MapPost("transactions/{id:guid}/exit", async (HttpContext c, Guid id, StationTransactionService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "ROUTING_TX_EXIT", "StationTransaction", id.ToString(), null, () => s.ConfirmAgvExitedAsync(id, ct), x => new { x.State }))).RequireAuthorization(admin);
        orders.MapPost("transactions/{id:guid}/observe", async (HttpContext c, Guid id, TransactionObserveRequest r, StationTransactionService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.ObserveHandshakeStepAsync(id, r.Step, ct))).RequireAuthorization(admin);
        orders.MapPost("transactions/{id:guid}/resolve", async (HttpContext c, Guid id, TransactionResolveRequest r, StationTransactionService s, CancellationToken ct) =>
        {
            StationTransactionState? resume = null;
            if (!string.IsNullOrWhiteSpace(r.ResumeState))
                resume = Enum.TryParse<StationTransactionState>(r.ResumeState, true, out var st) ? st : throw AppException.Validation("STATE_INVALID", "Unknown state.");
            return ApiResults.Ok(c, await Audited.RunAsync(c, "ROUTING_TX_RECONCILE", "StationTransaction", id.ToString(), null,
                () => s.ResolveReconciliationAsync(id, resume, c.Actor().Username, ct), x => new { x.State }));
        }).RequireAuthorization(admin);

        // the WCS side of step 1 (what the PLC runtime will call once the PLC engineer approves the mapping; Admin may inject a request for simulation)
        orders.MapPost("plc-requests", async (HttpContext c, PlcRequestInput r, IPlcRequestService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "ROUTING_PLC_REQUEST", "PlcRequest", $"{r.PlcId}:{r.RequestId}", null,
                () => s.HandleRequestAsync(new PlcRequestSnapshot(r.PlcId, r.PortId, r.RequestId, r.PickupRequest, r.DeliveryRequest, r.RackFull, r.RackEmpty, r.DestinationAvailable, r.RackType), ct),
                x => new { x.Outcome, x.Code }))).RequireAuthorization(admin);

        // ================================================================ Business Process administration (Admin)
        var proc = api.MapGroup("/processes").WithTags("Processes").RequireAuthorization(admin);
        proc.MapGet("", async (HttpContext c, ProcessAdminService s, CancellationToken ct) => ApiResults.Ok(c, await s.ListAsync(ct)));
        proc.MapGet("audit", async (HttpContext c, IAuditLog log, int? take, CancellationToken ct) =>
            ApiResults.Ok(c, await log.QueryAsync(new AuditQuery(null, null, null, null, null, 0, Math.Clamp(take ?? 100, 1, 500), "PROCESS_"), ct)));
        proc.MapGet("{id:guid}", async (HttpContext c, Guid id, ProcessAdminService s, CancellationToken ct) => ApiResults.Ok(c, await s.GetAsync(id, ct)));
        proc.MapPost("", async (HttpContext c, ProcessHeaderRequest r, ProcessAdminService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PROCESS_CREATE", "BusinessProcess", r.Code, null,
                () => s.CreateAsync(Header(r), c.Actor().Username, ct), x => new { x.Code, x.RequestType, x.RackKind }), StatusCodes.Status201Created));
        proc.MapPut("{id:guid}", async (HttpContext c, Guid id, ProcessHeaderRequest r, ProcessAdminService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PROCESS_UPDATE", "BusinessProcess", id.ToString(), null,
                () => s.UpdateAsync(id, Header(r), r.Enabled ?? true, ct), x => new { x.Code, x.Enabled })));
        proc.MapPost("{id:guid}/versions", async (HttpContext c, Guid id, NewVersionRequest r, ProcessAdminService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PROCESS_NEW_VERSION", "BusinessProcess", id.ToString(), null,
                () => s.NewVersionAsync(id, r.FromVersionId, c.Actor().Username, ct), x => new { x.Number }), StatusCodes.Status201Created));
        proc.MapPost("import-reference", async (HttpContext c, ProcessAdminService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PROCESS_IMPORT", "BusinessProcess", null, null,
                () => s.ImportReferenceAsync(ReferenceProcessCatalog.Load(), c.Actor().Username, ct), x => new { x.Imported, x.Skipped })));

        var ver = api.MapGroup("/process-versions").WithTags("Processes").RequireAuthorization(admin);
        ver.MapGet("{id:guid}", async (HttpContext c, Guid id, ProcessAdminService s, CancellationToken ct) => ApiResults.Ok(c, await s.GetVersionAsync(id, ct)));
        ver.MapPut("{id:guid}/definition", async (HttpContext c, Guid id, VersionDefinition def, ProcessAdminService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PROCESS_VERSION_SAVE", "BusinessProcessVersion", id.ToString(), null,
                () => s.SaveDefinitionAsync(id, def, ct), x => new { x.ProcessCode, x.Number, steps = x.Definition.Steps.Count })));
        ver.MapPost("{id:guid}/validate", async (HttpContext c, Guid id, ProcessAdminService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PROCESS_VALIDATE", "BusinessProcessVersion", id.ToString(), null,
                () => s.ValidateAsync(id, ct), x => new { x.ProcessCode, x.Number, x.State, issues = x.Issues.Count(i => i.Severity == IssueSeverity.Error) })));
        ver.MapPost("{id:guid}/publish", async (HttpContext c, Guid id, ProcessAdminService s, CancellationToken ct) =>
        {
            try
            {
                return ApiResults.Ok(c, await Audited.RunAsync(c, "PROCESS_PUBLISH", "BusinessProcessVersion", id.ToString(), null,
                    () => s.PublishAsync(id, c.Actor().Username, ct), x => new { x.ProcessCode, x.Number, x.State }));
            }
            catch (ProcessPublishRefusedException ex)
            {   // the Admin sees every failing check with its step
                return ApiResults.Fail(c, 422, ex.Code, ex.Message, ex.Issues);
            }
        });
        ver.MapPost("{id:guid}/disable", async (HttpContext c, Guid id, ProcessAdminService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PROCESS_DISABLE", "BusinessProcessVersion", id.ToString(), null,
                () => s.DisableAsync(id, ct), x => new { x.ProcessCode, x.Number, x.State })));

        // ================================================================ PLC provisioning (Admin)
        var plc = api.MapGroup("/plc").WithTags("PLC").RequireAuthorization(admin);
        plc.MapGet("stations", async (HttpContext c, PlcConfigService s, CancellationToken ct) => ApiResults.Ok(c, await s.ListStationsAsync(ct)));   // ports with their location link and approval counts
        plc.MapPost("connections", async (HttpContext c, AddPlcRequest r, PlcProvisioningService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PLC_ADD", "PlcConnection", r.PlcId, null,
                () => s.AddPlcAsync(new AddPlcCommand(r.PlcId, r.Name, r.IpAddress, r.Stations ?? []), c.Actor().Username, ct), x => new { x.PlcId, ports = x.Stations.Count }), StatusCodes.Status201Created));
        plc.MapPost("connections/{plcId}/stations", async (HttpContext c, string plcId, AddPortRequest r, PlcProvisioningService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PLC_PORT_ADD", "PlcStation", r.StationId, null,
                () => s.AddPortAsync(plcId, new AddPlcStationCommand(r.StationId, r.LocationCode), ct), x => new { x.StationId, x.PlcId }), StatusCodes.Status201Created));
        plc.MapPut("stations/{stationId}/link", async (HttpContext c, string stationId, PortLinkRequest r, PlcProvisioningService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PLC_PORT_LINK", "PlcStation", stationId, null,
                () => s.SetPortLinkAsync(stationId, r.LocationCode, r.Enabled, ct), x => new { x.StationId, x.LocationCode, x.Enabled })));
    }

    private static ProcessHeader Header(ProcessHeaderRequest r) => new(r.Code, r.Name, r.Description, r.RequestType, r.RackKind, r.TriggerLocationCodes ?? []);
}
