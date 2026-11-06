using Microsoft.Extensions.Options;
using WCS.Application.Plc;
using WCS.Domain.Plc;

namespace WCS.Api.Api;

public static partial class PlcEndpoints
{
    /// <summary>
    /// PLC topology, IO catalog and monitoring API. Configuration and diagnostics are Admin-only; the monitoring views
    /// (status, station list, IO values without addresses) are open to both roles. Nothing here sends a control signal by itself.
    /// </summary>
    public static void MapPlcApi(this RouteGroupBuilder api)
    {
        var admin = AuthSetup.AdminPolicy;
        var plc = api.MapGroup("/plc").WithTags("PLC");

        // ---- configuration (Admin)
        plc.MapGet("connections", async (HttpContext c, PlcConfigService s, CancellationToken ct) => ApiResults.Ok(c, await s.ListConnectionsAsync(ct)))
            .RequireAuthorization(admin);
        plc.MapPut("connections/{plcId}", async (HttpContext c, string plcId, UpdatePlcConnectionRequest r, PlcConfigService s, IOptions<PlcRuntimeOptions> o, CancellationToken ct) =>
        {
            var before = (await s.ListConnectionsAsync(ct)).FirstOrDefault(x => x.PlcId == plcId);
            var cmd = new UpdatePlcConnectionCommand(r.Name, r.IpAddress, r.Port, r.Protocol, r.NetworkNumber, r.PcNumber, r.ModuleIoNumber,
                r.PlcStationNumber, r.MonitoringTimer, r.TimeoutMs, r.PollingIntervalMs, r.Enabled);
            return ApiResults.Ok(c, await Audited.RunAsync(c, "PLC_CONNECTION_UPDATE", "PlcConnection", plcId, before,
                () => s.UpdateConnectionAsync(plcId, cmd, o.Value, c.Actor().Username, ct), res => res));
        }).RequireAuthorization(admin);
        plc.MapGet("connections/{plcId}/address-check", async (HttpContext c, string plcId, PlcConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.CheckPlcAsync(plcId, ct))).RequireAuthorization(admin);

        plc.MapGet("points", async (HttpContext c, string? plcId, string? stationId, PlcConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.ListPointsAsync(plcId, stationId, includeConfiguration: true, ct))).RequireAuthorization(admin);
        plc.MapPut("points/{id:guid}/address", async (HttpContext c, Guid id, SetPointAddressRequest r, PlcConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PLC_IO_ADDRESS_SET", "StationIoPoint", id.ToString(), null,
                () => s.SetAddressAsync(id, new SetPointAddressCommand(r.Address, r.WordLength, r.Encoding), ct), res => new { res.PlcId, res.StationId, res.SignalName, res.ActualAddress, res.WordLength, res.Encoding, res.ConfigStatus })))
            .RequireAuthorization(admin);
        plc.MapDelete("points/{id:guid}/address", async (HttpContext c, Guid id, PlcConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PLC_IO_ADDRESS_CLEAR", "StationIoPoint", id.ToString(), null, () => s.ClearAddressAsync(id, ct),
                res => new { res.PlcId, res.StationId, res.SignalName, res.ConfigStatus }))).RequireAuthorization(admin);
        plc.MapPost("points/{id:guid}/approve", async (HttpContext c, Guid id, PlcConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PLC_IO_ADDRESS_APPROVE", "StationIoPoint", id.ToString(), null,
                () => s.ApproveAsync(id, c.Actor().Username, ct), res => new { res.PlcId, res.StationId, res.SignalName, res.ActualAddress, res.ConfigStatus, res.ApprovedBy })))
            .RequireAuthorization(admin);
        plc.MapPut("points/{id:guid}/write-allowed", async (HttpContext c, Guid id, SetWriteAllowedRequest r, PlcConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "PLC_IO_WRITE_ALLOWED", "StationIoPoint", id.ToString(), null,
                () => s.SetWriteAllowedAsync(id, r.Allowed, ct), res => new { res.PlcId, res.StationId, res.SignalName, res.WriteAllowed })))
            .RequireAuthorization(admin);
        plc.MapGet("logs", async (HttpContext c, string? plcId, string? stationId, PlcLogLevel? minLevel, int? skip, int? take, PlcConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.LogsAsync(plcId, stationId, minLevel, skip ?? 0, take ?? 100, ct))).RequireAuthorization(admin);
    }
}
