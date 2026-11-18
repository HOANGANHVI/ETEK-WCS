using System.Text.RegularExpressions;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Plc;

namespace WCS.Application.Plc;

/// <summary>One signal of the Default IO Mapping Template (read only; the template itself is never modified by a PLC).</summary>
public sealed record TemplateSignal(int Seq, string Group, string SignalName, PlcSignalDirection Direction, PlcDataType DataType, string TemplateAddressReference, string Description);

public interface IPlcTemplateProvider
{
    IReadOnlyList<TemplateSignal> Signals { get; }
}

public sealed record AddPlcStationCommand(string StationId, string? LocationCode);
public sealed record AddPlcCommand(string PlcId, string Name, string IpAddress, IReadOnlyList<AddPlcStationCommand> Stations);

/// <summary>
/// PLC lifecycle beyond the four seeded PLCs (CR-010 6.1/6.2): add a PLC with its ports, give each port an INDEPENDENT copy of the Default IO Mapping Template,
/// link a port to a location. A new PLC is created disabled, with no network parameter and every IO point NEEDS_CONFIGURATION: nothing is approved, nothing can be
/// written. The number of PLCs is data; no code assumes four.
/// </summary>
public sealed partial class PlcProvisioningService(IPlcConfigRepository repo, ILocationRepository locations, IPlcTemplateProvider template, IUnitOfWork uow, IClock clock)
{
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,20}$")] private static partial Regex IdRegex();

    public Task<PlcConnectionView> AddPlcAsync(AddPlcCommand cmd, string? by, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var plcId = (cmd.PlcId ?? "").Trim();
        if (!IdRegex().IsMatch(plcId)) throw AppException.Validation("PLC_ID_INVALID", "PLC id: letters, digits, '-' or '_' (max 20).");
        if (string.IsNullOrWhiteSpace(cmd.Name)) throw AppException.Validation("PLC_NAME_REQUIRED", "PLC name is required.");
        if (string.IsNullOrWhiteSpace(cmd.IpAddress)) throw AppException.Validation("PLC_IP_REQUIRED", "IP address is required.");
        if (await repo.GetConnectionAsync(plcId, t) is not null) throw AppException.Conflict("PLC_EXISTS", $"PLC {plcId} already exists.");
        var stationIds = (cmd.Stations ?? []).Select(s => (s.StationId ?? "").Trim()).ToList();
        if (stationIds.Any(s => !IdRegex().IsMatch(s))) throw AppException.Validation("STATION_ID_INVALID", "Port id: letters, digits, '-' or '_' (max 20).");
        if (stationIds.Distinct(StringComparer.Ordinal).Count() != stationIds.Count) throw AppException.Validation("STATION_DUPLICATE", "Port ids must be unique.");

        var now = clock.UtcNow;
        var c = new PlcConnection(plcId, cmd.Name.Trim(), cmd.IpAddress.Trim(), now);   // disabled, parameters NEEDS_CONFIGURATION
        repo.AddConnection(c);
        var order = 0;
        var ids = new List<string>();
        foreach (var s in cmd.Stations ?? [])
        {
            var st = await AddPortCoreAsync(plcId, s.StationId.Trim(), s.LocationCode, ++order, now, t);
            ids.Add(st.Id);
        }
        return PlcConfigService.ToView(c, ids);   // built from the new objects: nothing is saved yet inside the unit of work
    }, ct);

    public Task<PlcStationView> AddPortAsync(string plcId, AddPlcStationCommand cmd, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        if (await repo.GetConnectionAsync(plcId, t) is null) throw AppException.NotFound("PLC_NOT_FOUND", $"PLC {plcId} not found.");
        var id = (cmd.StationId ?? "").Trim();
        if (!IdRegex().IsMatch(id)) throw AppException.Validation("STATION_ID_INVALID", "Port id: letters, digits, '-' or '_' (max 20).");
        var order = (await repo.ListStationsAsync(t)).Where(s => s.PlcId == plcId).Select(s => s.Order).DefaultIfEmpty(0).Max() + 1;
        var st = await AddPortCoreAsync(plcId, id, cmd.LocationCode, order, clock.UtcNow, t);
        return new PlcStationView(st.Id, st.PlcId, st.Order, st.LocationCode, st.Enabled, st.Role, template.Signals.Count, 0, 0);
    }, ct);

    private async Task<PlcStation> AddPortCoreAsync(string plcId, string stationId, string? locationCode, int order, DateTimeOffset now, CancellationToken ct)
    {
        if (await repo.GetStationAsync(stationId, ct) is not null) throw AppException.Conflict("STATION_EXISTS", $"Port {stationId} already exists.");
        var station = new PlcStation(stationId, plcId, order, now);
        if (!string.IsNullOrWhiteSpace(locationCode))
        {
            if (await locations.GetByCodeAsync(locationCode.Trim(), ct) is null) throw AppException.NotFound("LOCATION_NOT_FOUND", $"Location {locationCode} not found.");
            station.Update(locationCode.Trim(), true, now);
        }
        repo.AddStation(station);
        // an independent copy of the whole template for THIS port: editing it never touches another port or the template
        foreach (var sig in template.Signals)
            repo.AddPoint(new StationIoPoint(plcId, stationId, sig.Seq, sig.Group, sig.SignalName, sig.Direction, sig.DataType, sig.TemplateAddressReference, sig.Description, now));
        return station;
    }

    /// <summary>Links (or unlinks) a port to a WCS location and enables/disables it. Link only; this never changes an address or an approval.</summary>
    public Task<PlcStationView> SetPortLinkAsync(string stationId, string? locationCode, bool enabled, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var st = await repo.GetStationAsync(stationId, t) ?? throw AppException.NotFound("STATION_NOT_FOUND", $"Port {stationId} not found.");
        string? code = string.IsNullOrWhiteSpace(locationCode) ? null : locationCode.Trim();
        if (code is not null)
        {
            if (await locations.GetByCodeAsync(code, t) is null) throw AppException.NotFound("LOCATION_NOT_FOUND", $"Location {code} not found.");
            var other = (await repo.ListStationsAsync(t)).FirstOrDefault(s => s.Id != stationId && s.LocationCode == code);
            if (other is not null) throw AppException.Conflict("LOCATION_ALREADY_LINKED", $"Location {code} is already linked to port {other.Id}.");
        }
        st.Update(code, enabled, clock.UtcNow);
        var mine = await repo.ListPointsAsync(st.PlcId, st.Id, t);
        return new PlcStationView(st.Id, st.PlcId, st.Order, st.LocationCode, st.Enabled, st.Role, mine.Count,
            mine.Count(p => p.Status != IoConfigStatus.NeedsConfiguration), mine.Count(p => p.Status == IoConfigStatus.Approved));
    }, ct);
}
