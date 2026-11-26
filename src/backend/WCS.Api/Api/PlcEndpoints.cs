using System.Text.Json;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Config;
using WCS.Application.Plc;
using WCS.Domain.Common;
using WCS.Domain.Plc;

namespace WCS.Api.Api;

public static partial class PlcEndpoints
{
    private static bool IsAdmin(HttpContext c) => c.User.IsInRole(nameof(WCS.Domain.Users.UserRole.Admin));

    private static void MarkDeprecated(HttpContext c) { c.Response.Headers["Deprecation"] = "true"; c.Response.Headers["Warning"] = "299 - Deprecated by CR-012: reading no longer requires approval"; }

    /// <summary>PLCs allowed to use the bulk read-approval: configuration <c>Plc:BulkApproval:AllowedPlcIds</c>, default PLC-04 only; an explicit empty list disables it.</summary>
    private static IReadOnlyList<string> BulkAllowed(IConfiguration cfg) =>
        cfg.GetSection(BulkApprovalPolicy.ConfigKey).Exists() ? cfg.GetSection(BulkApprovalPolicy.ConfigKey).Get<string[]>() ?? [] : BulkApprovalPolicy.Default;

    /// <summary>
    /// PLC topology, IO catalog and monitoring API. Configuration, addresses, diagnostics, logs and every write facility are Admin-only;
    /// the monitoring views (state, station list, IO values and quality without addresses or network details) are open to both roles.
    /// Nothing here sends a control signal by itself.
    /// </summary>
    /// <summary>CR-044: a write request that authentication or the role check refuses never reaches the endpoint; it is recorded here. It changes no response.</summary>
    public static void UsePlcWriteDenialAudit(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        await next();
        if (!HttpMethods.IsPost(ctx.Request.Method) || ctx.Response.StatusCode is not (401 or 403)
            || !(ctx.Request.Path.Value ?? "").EndsWith("/test-write", StringComparison.OrdinalIgnoreCase)) return;
        var path = ctx.Request.Path.Value!;
        await ctx.RequestServices.GetRequiredService<WCS.Application.Auth.IAuditLog>().WriteAsync(new WCS.Application.Auth.AuditRecord("PLC_WRITE_DENIED", ctx.User.Identity?.Name, null,
            "PlcWriteRequest", path.Length > 200 ? path[..200] : path, WCS.Domain.Users.AuditResult.Denied, $"HTTP {ctx.Response.StatusCode}: not authorized to write to a PLC",
            IpAddress: ctx.Connection.RemoteIpAddress?.ToString()), CancellationToken.None);
    });

    public static void MapPlcApi(this RouteGroupBuilder api)
    {
        var admin = AuthSetup.AdminPolicy;
        var plc = api.MapGroup("/plc").WithTags("PLC");

        // ---------------------------------------------------------------------------------------------- configuration (Admin)
        plc.MapGet("connections", async (HttpContext c, PlcConfigService s, CancellationToken ct) => ApiResults.Ok(c, await s.ListConnectionsAsync(ct)))
            .RequireAuthorization(admin);
        plc.MapPut("connections/{plcId}", async (HttpContext c, string plcId, UpdatePlcConnectionRequest r, PlcConfigService s, IOptions<PlcRuntimeOptions> o, IPlcRuntime rt, CancellationToken ct) =>
        {
            var before = (await s.ListConnectionsAsync(ct)).FirstOrDefault(x => x.PlcId == plcId);
            var cmd = new UpdatePlcConnectionCommand(r.Name, r.IpAddress, r.Port, r.Protocol, r.NetworkNumber, r.PcNumber, r.ModuleIoNumber,
                r.PlcStationNumber, r.MonitoringTimer, r.TimeoutMs, r.PollingIntervalMs, r.Enabled,
                r.ConnectTimeoutMs, r.ReadTimeoutMs, r.WriteTimeoutMs, r.XyAddressBase, r.WAddressBase);
            var view = await Audited.RunAsync(c, "PLC_CONNECTION_UPDATE", "PlcConnection", plcId, before,
                () => s.UpdateConnectionAsync(plcId, cmd, o.Value, c.Actor().Username, ct), res => res);
            await rt.ReloadPlcAsync(plcId, ct);
            return ApiResults.Ok(c, view);
        }).RequireAuthorization(admin);
        plc.MapGet("connections/{plcId}/address-check", async (HttpContext c, string plcId, PlcConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.CheckPlcAsync(plcId, ct))).RequireAuthorization(admin);

        // Bulk READ approval of one commissioning PLC (default PLC-04 only; Plc:BulkApproval:AllowedPlcIds). It uses the existing approval (StationIoPoint.Approve):
        // it never changes an address or data type, never allows a write, never touches another PLC and sends nothing to the PLC. Never runs on its own.
        // CR-012: DEPRECATED. Reading no longer needs approval (a valid address is pollable). Approval still governs write permission and port-mapping approval for processes.
        // Kept for backward compatibility; the UI no longer offers it.
        plc.MapGet("connections/{plcId}/bulk-approval", async (HttpContext c, string plcId, PlcConfigService s, IConfiguration cfg, CancellationToken ct) =>
        {
            MarkDeprecated(c);
            return ApiResults.Ok(c, await s.PreviewBulkApprovalAsync(plcId, BulkAllowed(cfg), ct));
        }).RequireAuthorization(admin);
        plc.MapPost("connections/{plcId}/bulk-approval", async (HttpContext c, string plcId, BulkApprovalRequest r, PlcConfigService s, IConfiguration cfg, IServiceScopeFactory scopes, IPlcRuntime rt, CancellationToken ct) =>
        {
            MarkDeprecated(c);
            if (r.Confirm != true || r.ExpectedEligible is null)
                throw AppException.Validation("BULK_APPROVAL_NOT_CONFIRMED", "Cần xác nhận và số điểm đủ điều kiện đã xem ở bản xem trước.");
            var allowed = BulkAllowed(cfg);
            var result = await Audited.RunAsync(c, "PLC_IO_BULK_APPROVE", "PlcConnection", plcId, null, async () =>
            {
                var applied = await s.BulkApproveAsync(plcId, c.Actor().Username, allowed, r.ExpectedEligible, ct);
                await using var scope = scopes.CreateAsyncScope();                  // a fresh context: the result is checked against the database, not the tracker
                return await scope.ServiceProvider.GetRequiredService<PlcConfigService>().VerifyBulkApprovalAsync(applied, ct);
            }, res => new
            {
                res.Total, res.Approved, res.AlreadyApproved, res.NotEligible, res.Failed, res.VerifiedPollable,
                approved = res.Items.Where(i => i.Outcome == BulkOutcome.Approved).Select(i => $"{i.StationId}/{i.SignalName}={i.Address}").ToList(),
                failed = res.Items.Where(i => i.Outcome == BulkOutcome.Failed).Select(i => $"{i.StationId}/{i.SignalName}:{i.Code}").ToList(),
            }, detail: $"expectedEligible={r.ExpectedEligible}");
            await rt.ReloadPlcAsync(plcId, ct);
            return ApiResults.Ok(c, result);
        }).RequireAuthorization(admin);

        // Read-only reachability check: never sends a PLC command. Simulator mode and an unconfirmed configuration never touch the network.
        plc.MapPost("connections/{plcId}/test", async (HttpContext c, string plcId, PlcConfigService s, IOptions<PlcRuntimeOptions> o, IConnectionProbe probe, CancellationToken ct) =>
        {
            var conn = (await s.ListConnectionsAsync(ct)).FirstOrDefault(x => x.PlcId == plcId) ?? throw AppException.NotFound("PLC_NOT_FOUND", $"Không tìm thấy {plcId}.");
            var opt = o.Value;
            ProbeResult result;
            if (opt.Mode == PlcRuntimeMode.Simulator)
                result = new ProbeResult(ConnectionTestOutcome.Simulated, "Simulated", "[PROBE_SIMULATOR_MODE] Chế độ mô phỏng: không có kết nối vật lý, không dùng mạng.");
            else if (!opt.AllowLiveConnection)
                result = new ProbeResult(ConnectionTestOutcome.NotExecuted, "None", "[LIVE_CONNECTION_DISABLED] Plc:AllowLiveConnection = false: không mở kết nối tới PLC thật.");
            else if (conn.MissingParameters.Count > 0)
                result = new ProbeResult(ConnectionTestOutcome.NotExecuted, "None", "[PLC_PARAMS_MISSING] " + string.Join(", ", conn.MissingParameters));
            else
                result = await probe.ProbeAsync(ConnectionKind.Plc, conn.IpAddress, conn.Port!.Value, TimeSpan.FromMilliseconds(conn.TimeoutMs!.Value), ct);
            return ApiResults.Ok(c, await Audited.RunAsync(c, "PLC_CONNECTION_TEST", "PlcConnection", plcId, null, () => Task.FromResult(result), res => new { res.Outcome, res.Mode }));
        }).RequireAuthorization(admin);

        plc.MapGet("points", async (HttpContext c, string? plcId, string? stationId, bool? includeRetired, PlcConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.ListPointsAsync(plcId, stationId, includeConfiguration: true, ct, includeRetired == true))).RequireAuthorization(admin);
        // CR-046: what the IO map in use says (source, PLC-level rows, stations the database still has from an earlier map). Read only; nothing here is a configured address.
        plc.MapGet("io-map", async (HttpContext c, PlcConfigService s, WCS.Infrastructure.Plc.PlcCatalog cat, CancellationToken ct) =>
        {
            var retired = (await s.ListPointsAsync(null, null, true, ct, includeRetired: true)).GroupBy(p => (p.PlcId, p.StationId)).Where(g => g.All(p => p.InIoMap == false))
                .Select(g => new { g.Key.PlcId, g.Key.StationId, points = g.Count(), withAddress = g.Count(p => p.ActualAddress is not null) }).OrderBy(x => x.PlcId).ThenBy(x => x.StationId).ToList();
            return ApiResults.Ok(c, new
            {
                sourceFile = Path.GetFileName(cat.Source.File), sourceSha256 = cat.Source.Sha256, confirmed = false,
                plcs = cat.Plcs.Select(p => new { p.PlcId, p.DocumentLabel, p.IpAddress, p.Stations }),
                plcSignals = cat.PlcSignals.Select(x => new { x.PlcId, x.SignalName, x.Direction, x.DataType, x.Address, x.Values, x.Meaning, x.Spare }),
                stationsNotInMap = retired,
            });
        }).RequireAuthorization(admin);
        plc.MapPut("points/{id:guid}/address", async (HttpContext c, Guid id, SetPointAddressRequest r, PlcConfigService s, IPlcRuntime rt, CancellationToken ct) =>
        {
            var view = await Audited.RunAsync(c, "PLC_IO_ADDRESS_SET", "StationIoPoint", id.ToString(), null,
                () => s.SetAddressAsync(id, new SetPointAddressCommand(r.Address, r.WordLength, r.Encoding), ct),
                res => new { res.PlcId, res.StationId, res.SignalName, res.ActualAddress, res.WordLength, res.Encoding, res.ConfigStatus });
            await rt.ReloadPlcAsync(view.PlcId, ct);
            return ApiResults.Ok(c, view);
        }).RequireAuthorization(admin);
        plc.MapDelete("points/{id:guid}/address", async (HttpContext c, Guid id, PlcConfigService s, IPlcRuntime rt, CancellationToken ct) =>
        {
            var view = await Audited.RunAsync(c, "PLC_IO_ADDRESS_CLEAR", "StationIoPoint", id.ToString(), null, () => s.ClearAddressAsync(id, ct),
                res => new { res.PlcId, res.StationId, res.SignalName, res.ConfigStatus });
            await rt.ReloadPlcAsync(view.PlcId, ct);
            return ApiResults.Ok(c, view);
        }).RequireAuthorization(admin);
        plc.MapPost("points/{id:guid}/approve", async (HttpContext c, Guid id, PlcConfigService s, IPlcRuntime rt, CancellationToken ct) =>
        {
            var view = await Audited.RunAsync(c, "PLC_IO_ADDRESS_APPROVE", "StationIoPoint", id.ToString(), null,
                () => s.ApproveAsync(id, c.Actor().Username, ct), res => new { res.PlcId, res.StationId, res.SignalName, res.ActualAddress, res.ConfigStatus, res.ApprovedBy });
            await rt.ReloadPlcAsync(view.PlcId, ct);
            return ApiResults.Ok(c, view);
        }).RequireAuthorization(admin);
        plc.MapPut("points/{id:guid}/write-allowed", async (HttpContext c, Guid id, SetWriteAllowedRequest r, PlcConfigService s, IPlcRuntime rt, CancellationToken ct) =>
        {
            var view = await Audited.RunAsync(c, "PLC_IO_WRITE_ALLOWED", "StationIoPoint", id.ToString(), null,
                () => s.SetWriteAllowedAsync(id, r.Allowed, c.Actor().Username, ct), res => new { res.PlcId, res.StationId, res.SignalName, res.WriteAllowed });
            await rt.ReloadPlcAsync(view.PlcId, ct);
            return ApiResults.Ok(c, view);
        }).RequireAuthorization(admin);
        // CR-014: write approval of one point (Admin). Independent of reading and of the handshake; it does not send anything to a PLC.
        plc.MapPost("points/{id:guid}/write-approval", async (HttpContext c, Guid id, WriteApprovalRequest r, PlcConfigService s, IPlcRuntime rt, CancellationToken ct) =>
        {
            var approve = r.Decision.Equals("Approve", StringComparison.OrdinalIgnoreCase);
            if (!approve && !r.Decision.Equals("Reject", StringComparison.OrdinalIgnoreCase))
                throw AppException.Validation("WRITE_DECISION_INVALID", "Quyết định phải là Approve hoặc Reject.");
            var view = await Audited.RunAsync(c, approve ? "PLC_IO_WRITE_APPROVE" : "PLC_IO_WRITE_REJECT", "StationIoPoint", id.ToString(), null,
                () => s.DecideWriteAsync(id, approve, c.Actor().Username, r.Note, ct),
                res => new { res.PlcId, res.StationId, res.SignalName, address = res.ActualAddress, res.DataType, res.WriteApproval, res.WriteAllowed, decidedBy = res.WriteDecidedBy, decidedAt = res.WriteDecidedAt },
                detail: r.Note);
            await rt.ReloadPlcAsync(view.PlcId, ct);
            return ApiResults.Ok(c, view);
        }).RequireAuthorization(admin);
        plc.MapGet("logs", async (HttpContext c, string? plcId, string? stationId, PlcLogLevel? minLevel, int? skip, int? take, PlcConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.LogsAsync(plcId, stationId, minLevel, skip ?? 0, take ?? 100, ct))).RequireAuthorization(admin);

        // ---------------------------------------------------------------------------------------------- monitoring (Admin + Operator)
        plc.MapGet("overview", async (HttpContext c, PlcConfigService s, IPlcRuntime rt, CancellationToken ct) =>
        {
            var adm = IsAdmin(c);
            var stations = (await s.ListStationsAsync(ct)).Select(st =>
            {
                var snap = rt.Station(st.StationId, false);
                return new
                {
                    st.StationId, st.PlcId, st.Order, st.Role,
                    locationCode = adm ? st.LocationCode : null,
                    total = snap?.Total ?? st.PointCount,
                    // CR-046: the simulator answers for points without an address too; "configured" always means that an address exists
                    configured = rt.Info().EffectiveMode == nameof(PlcRuntimeMode.Simulator) ? st.ConfiguredPoints : snap?.Configured ?? st.ConfiguredPoints, good = snap?.Good ?? 0,
                    approved = adm ? st.ApprovedPoints : (int?)null,
                };
            }).ToList();
            return ApiResults.Ok(c, new { runtime = rt.Info(), plcs = rt.Workers(adm), stations });
        });
        plc.MapGet("stations/{stationId}/io", (HttpContext c, string stationId, IPlcRuntime rt) =>
            rt.Station(stationId, IsAdmin(c)) is { } snap ? ApiResults.Ok(c, snap) : ApiResults.Fail(c, 404, "STATION_NOT_FOUND", $"Không tìm thấy trạm {stationId}."));

        // CR-020: the PLC Monitoring list - every IO point of every PLC with its live value, communication status and (Admin only) address and write approval. It reads the values the workers already
        // poll (no PLC traffic is generated here). Operators get no address and no approval data.
        plc.MapGet("monitor/points", async (HttpContext c, string? plcId, string? stationId, PlcConfigService s, IPlcRuntime rt, IOptions<PlcRuntimeOptions> o, IClock clock, CancellationToken ct) =>
        {
            var adm = IsAdmin(c);
            var now = clock.UtcNow; var staleAfter = TimeSpan.FromSeconds(Math.Max(1, o.Value.StaleAfterSeconds));
            var config = (await s.ListPointsAsync(plcId, stationId, adm, ct)).ToDictionary(p => p.Id);
            var rows = rt.Stations(adm).Where(st => (plcId is null || st.PlcId == plcId) && (stationId is null || st.StationId == stationId)).SelectMany(st => st.Points)
                .Where(p => config.ContainsKey(p.Id)).Select(p =>
                {
                    var cfg = config[p.Id];
                    return new MonitorPointView(p.Id, p.PlcId, p.StationId, p.Seq, p.Group, p.SignalName, cfg.Description, p.Direction, p.DataType, p.Value, p.Quality,
                        PlcMonitor.StatusOf(p.Quality, p.LastUpdated, now, staleAfter), p.LastUpdated, p.Address, p.Error,
                        adm ? cfg.WriteApproval : null, adm ? cfg.WriteSupported : null, WritePolicy.IsBlockedSignal(p.SignalName, p.Group), cfg.WordLength, cfg.Encoding,
                        !UnconfirmedSignals.Contains(p.SignalName), adm ? cfg.TemplateAddressReference : null, p.HasAddress);   // CR-046
                }).OrderBy(p => p.PlcId).ThenBy(p => p.StationId).ThenBy(p => p.Seq).ToList();
            return ApiResults.Ok(c, new { serverTime = now, staleAfterSeconds = (int)staleAfter.TotalSeconds, points = rows });
        });
        // CR-020: immediate read of ONE point (Admin and Operator: it only reads). It goes through the PLC's own worker and connection (see PlcWorker.EnqueueReadAsync); concurrent requests for the
        // same point share one PLC read. A failed read is a normal result (success=false with the cause), not an HTTP error.
        plc.MapPost("stations/{stationId}/signals/{signal}/read", async (HttpContext c, string stationId, string signal, IPlcRuntime rt, CancellationToken ct) =>
        {
            var handler = rt.Handler(stationId) ?? throw AppException.NotFound("STATION_NOT_FOUND", $"Không tìm thấy trạm {stationId}.");
            var point = handler.Find(signal) ?? throw AppException.NotFound("IO_POINT_NOT_FOUND", "Không tìm thấy IO point.");
            var r = await rt.ReadNowAsync(point.PlcId, stationId, signal, ct);
            var snap = rt.Station(stationId, IsAdmin(c))?.Points.FirstOrDefault(p => p.Id == point.Id);
            return ApiResults.Ok(c, new { success = r.Success, code = r.Code, message = r.Message, value = r.Value?.Value, quality = snap?.Quality, lastUpdated = snap?.LastUpdated });
        });

        plc.MapGet("handshake", (HttpContext c, HandshakeCoordinator hs) => ApiResults.Ok(c, hs.States()));
        plc.MapGet("handshake/catalog", (HttpContext c, WCS.Infrastructure.Plc.PlcCatalog cat) =>
        {
            var covered = cat.Handshake.Select(h => h.SignalName).Distinct().ToHashSet();
            return ApiResults.Ok(c, new
            {
                rows = cat.Handshake.Count, pending = cat.Handshake.Count(h => h.ApprovalStatus == "PENDING"), approved = cat.Handshake.Count(h => h.ApprovalStatus == "APPROVED"),
                signalsWithoutDefinition = cat.Template.Select(t => t.SignalName).Where(n => !covered.Contains(n)).ToArray(), sourceSha256 = cat.Source.Sha256,
            });
        }).RequireAuthorization(admin);

        // ---------------------------------------------------------------------------------------------- guarded write (Admin)
        // CR-015: available in every environment, Production included. What guards it is NOT the environment but, in this order: Admin role, the per-point write approval,
        // the value validation, WritePolicy (mode REAL_READ_WRITE + AllowLiveConnection + Write.Enabled + never the safety/permit signals) and the MC driver. Every call is audited.
        plc.MapPost("stations/{stationId}/signals/{signal}/test-write", async (HttpContext c, string stationId, string signal, TestWriteRequest r, PlcConfigService s, IPlcRuntime rt, CancellationToken ct) =>
        {
            // CR-044: the lookup and the value check are inside the audited operation, so a request refused by them is audited too
            var point = (await s.ListPointsAsync(null, stationId, true, ct)).FirstOrDefault(p => p.SignalName == signal);
            PointValue? value = null;
            var result = await Audited.RunAsync(c, "PLC_TEST_WRITE", "StationIoPoint", point?.Id.ToString() ?? $"{stationId}/{signal}", null,
                async () =>
                {
                    if (point is null) throw AppException.NotFound("IO_POINT_NOT_FOUND", "Không tìm thấy IO point.");
                    var type = Enum.Parse<PlcDataType>(point.DataType);
                    value = ParseValue(type, r.Value);
                    var shape = WriteValueValidator.Check(type, point.WordLength, point.Encoding, value);   // CR-014: reject a wrong value here, before the runtime
                    if (!shape.Ok) throw AppException.Validation(shape.Code!, shape.Message!);
                    var w = await rt.WriteAsync(point.PlcId, stationId, signal, value, c.Actor().Username, ct);
                    if (!w.Success) throw AppException.Rejected(w.Code ?? "WRITE_REFUSED", w.Message ?? "Lệnh ghi bị từ chối.");
                    // CR-020 read-back: a successful write request is not proof; read the point once from the PLC and compare. Never retries the write; a failed read-back is reported as UNAVAILABLE.
                    var back = PlcMonitor.Verify(value, await rt.ReadNowAsync(point.PlcId, stationId, signal, ct));
                    return new { success = w.Success, code = w.Code, message = w.Message, isAmbiguous = w.IsAmbiguous, readBack = back };
                }, res => new { point!.PlcId, stationId, signal, value = value!.ToString(), readBack = res.readBack.Status });
            return ApiResults.Ok(c, result);
        }).RequireAuthorization(admin);

        // ---------------------------------------------------------------------------------------------- simulator controls (Admin, Simulator mode only)
        var sim = plc.MapGroup("/simulator").RequireAuthorization(admin);
        sim.MapPost("value", async (HttpContext c, SimulatorValueRequest r, PlcConfigService s, ISimulatorControl ctl, IOptions<PlcRuntimeOptions> o, IWebHostEnvironment env, CancellationToken ct) =>
        {
            RequireNotProduction(env); RequireSimulator(o.Value);
            var point = (await s.ListPointsAsync(r.PlcId, r.StationId, true, ct)).FirstOrDefault(p => p.SignalName == r.Signal) ?? throw AppException.NotFound("IO_POINT_NOT_FOUND", "Không tìm thấy IO point.");
            var value = ParseValue(Enum.Parse<PlcDataType>(point.DataType), r.Value);
            ctl.SetValue(r.PlcId, r.StationId, r.Signal, value);
            await Audited.WriteAsync(c, "PLC_SIMULATOR_SET_VALUE", "StationIoPoint", point.Id.ToString(), $"{r.StationId}/{r.Signal} = {value}");
            return ApiResults.Ok(c, new { r.PlcId, r.StationId, r.Signal, value = value.ToString() });
        });
        sim.MapPost("plc-online", async (HttpContext c, SimulatorOnlineRequest r, ISimulatorControl ctl, IOptions<PlcRuntimeOptions> o, IWebHostEnvironment env) =>
        {
            RequireNotProduction(env); RequireSimulator(o.Value);
            ctl.SetPlcOnline(r.PlcId, r.Online);
            await Audited.WriteAsync(c, "PLC_SIMULATOR_ONLINE", "PlcConnection", r.PlcId, r.Online ? "online" : "offline");
            return ApiResults.Ok(c, new { r.PlcId, r.Online });
        });
        sim.MapPost("station-fault", async (HttpContext c, SimulatorStationFaultRequest r, ISimulatorControl ctl, IOptions<PlcRuntimeOptions> o, IWebHostEnvironment env) =>
        {
            RequireNotProduction(env); RequireSimulator(o.Value);
            ctl.SetStationFault(r.PlcId, r.StationId, r.Failing);
            await Audited.WriteAsync(c, "PLC_SIMULATOR_STATION_FAULT", "PlcStation", r.StationId, r.Failing ? "failing" : "healthy");
            return ApiResults.Ok(c, new { r.PlcId, r.StationId, r.Failing });
        });
    }

    /// <summary>Simulator controls are engineering tools of the in-memory simulator: refused in the Production environment (CR-004). The PLC write is NOT among them (CR-015).</summary>
    private static void RequireNotProduction(IWebHostEnvironment env)
    {
        if (env.IsProduction()) throw AppException.Conflict("NOT_AVAILABLE_IN_PRODUCTION", "Chức năng kỹ thuật này không khả dụng trong môi trường vận hành.");
    }

    private static void RequireSimulator(PlcRuntimeOptions o)
    {
        if (o.Mode != PlcRuntimeMode.Simulator) throw AppException.Conflict("NOT_SIMULATOR_MODE", "Chỉ dùng được ở chế độ SIMULATOR.");
    }

    /// <summary>Converts the JSON value of a request into the typed value of the signal (strict: no silent coercion).</summary>
    private static PointValue ParseValue(PlcDataType type, JsonElement e)
    {
        try
        {
            return type switch
            {
                PlcDataType.Bool => e.ValueKind switch
                {
                    JsonValueKind.True => PointValue.OfBool(true), JsonValueKind.False => PointValue.OfBool(false),
                    _ => throw new FormatException(),
                },
                PlcDataType.Int => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? PointValue.OfInt(n) : throw new FormatException(),   // a fraction or a number beyond 32 bits is not an INT
                _ => e.ValueKind == JsonValueKind.String ? PointValue.OfString(e.GetString()!) : throw new FormatException(),
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
        {
            throw AppException.Validation("VALUE_INVALID", $"Giá trị không hợp lệ cho kiểu {type}.");
        }
    }
}
