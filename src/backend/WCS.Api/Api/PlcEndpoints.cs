using System.Text.Json;
using Microsoft.Extensions.Options;
using WCS.Application.Common;
using WCS.Application.Config;
using WCS.Application.Plc;
using WCS.Domain.Common;
using WCS.Domain.Plc;

namespace WCS.Api.Api;

public static partial class PlcEndpoints
{
    private static bool IsAdmin(HttpContext c) => c.User.IsInRole(nameof(WCS.Domain.Users.UserRole.Admin));

    /// <summary>
    /// PLC topology, IO catalog and monitoring API. Configuration, addresses, diagnostics, logs and every write facility are Admin-only;
    /// the monitoring views (state, station list, IO values and quality without addresses or network details) are open to both roles.
    /// Nothing here sends a control signal by itself.
    /// </summary>
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

        plc.MapGet("points", async (HttpContext c, string? plcId, string? stationId, PlcConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.ListPointsAsync(plcId, stationId, includeConfiguration: true, ct))).RequireAuthorization(admin);
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
                () => s.SetWriteAllowedAsync(id, r.Allowed, ct), res => new { res.PlcId, res.StationId, res.SignalName, res.WriteAllowed });
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
                    total = snap?.Total ?? st.PointCount, configured = snap?.Configured ?? st.ConfiguredPoints, good = snap?.Good ?? 0,
                    approved = adm ? st.ApprovedPoints : (int?)null,
                };
            }).ToList();
            return ApiResults.Ok(c, new { runtime = rt.Info(), plcs = rt.Workers(adm), stations });
        });
        plc.MapGet("stations/{stationId}/io", (HttpContext c, string stationId, IPlcRuntime rt) =>
            rt.Station(stationId, IsAdmin(c)) is { } snap ? ApiResults.Ok(c, snap) : ApiResults.Fail(c, 404, "STATION_NOT_FOUND", $"Không tìm thấy trạm {stationId}."));

        plc.MapGet("handshake", (HttpContext c, HandshakeCoordinator hs) => ApiResults.Ok(c, hs.States()));
        plc.MapGet("handshake/catalog", (HttpContext c) =>
        {
            var cat = WCS.Infrastructure.Plc.PlcCatalog.LoadEmbedded();
            var covered = cat.Handshake.Select(h => h.SignalName).Distinct().ToHashSet();
            return ApiResults.Ok(c, new
            {
                rows = cat.Handshake.Count, pending = cat.Handshake.Count(h => h.ApprovalStatus == "PENDING"), approved = cat.Handshake.Count(h => h.ApprovalStatus == "APPROVED"),
                signalsWithoutDefinition = cat.Template.Select(t => t.SignalName).Where(n => !covered.Contains(n)).ToArray(), sourceSha256 = cat.Source.Sha256,
            });
        }).RequireAuthorization(admin);

        // ---------------------------------------------------------------------------------------------- guarded test write (Admin)
        plc.MapPost("stations/{stationId}/signals/{signal}/test-write", async (HttpContext c, string stationId, string signal, TestWriteRequest r, PlcConfigService s, IPlcRuntime rt, IWebHostEnvironment env, CancellationToken ct) =>
        {
            RequireNotProduction(env);
            var point = (await s.ListPointsAsync(null, stationId, true, ct)).FirstOrDefault(p => p.SignalName == signal) ?? throw AppException.NotFound("IO_POINT_NOT_FOUND", "Không tìm thấy IO point.");
            var type = Enum.Parse<PlcDataType>(point.DataType);
            var value = ParseValue(type, r.Value);
            var result = await Audited.RunAsync(c, "PLC_TEST_WRITE", "StationIoPoint", point.Id.ToString(), null,
                async () =>
                {
                    var w = await rt.WriteAsync(point.PlcId, stationId, signal, value, c.Actor().Username, ct);
                    if (!w.Success) throw AppException.Rejected(w.Code ?? "WRITE_REFUSED", w.Message ?? "Lệnh ghi bị từ chối.");
                    return w;
                }, _ => new { point.PlcId, stationId, signal, value = value.ToString() });
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

    /// <summary>Test-write and simulator controls are engineering tools: refused in the Production environment (UI cleanup follow-up to CR-004).</summary>
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
                    JsonValueKind.Number when e.GetInt32() is 0 or 1 => PointValue.OfBool(e.GetInt32() == 1),
                    _ => throw new FormatException(),
                },
                PlcDataType.Int => e.ValueKind == JsonValueKind.Number ? PointValue.OfInt(e.GetInt32()) : throw new FormatException(),
                _ => e.ValueKind == JsonValueKind.String ? PointValue.OfString(e.GetString()!) : throw new FormatException(),
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
        {
            throw AppException.Validation("VALUE_INVALID", $"Giá trị không hợp lệ cho kiểu {type}.");
        }
    }
}
