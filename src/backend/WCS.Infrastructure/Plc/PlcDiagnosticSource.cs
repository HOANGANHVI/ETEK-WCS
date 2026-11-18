using Microsoft.EntityFrameworkCore;
using WCS.Application.Diagnostics;
using WCS.Domain.Plc;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Plc;

/// <summary>Read-only queries (AsNoTracking) for the PLC diagnostic export. Uses the request's own database context; it never writes.</summary>
internal sealed class PlcDiagnosticSource(WcsDbContext db) : IPlcDiagnosticSource
{
    public async Task<IReadOnlyList<PlcConnectionDiag>> ConnectionsAsync(string? plcId, CancellationToken ct)
    {
        var conns = await db.PlcConnections.AsNoTracking().Where(c => plcId == null || c.Id == plcId).OrderBy(c => c.Id).ToListAsync(ct);
        var ids = conns.Select(c => c.Id).ToList();
        var stations = await db.PlcStations.AsNoTracking().Where(s => ids.Contains(s.PlcId)).OrderBy(s => s.PlcId).ThenBy(s => s.Order).ToListAsync(ct);
        var points = await db.StationIoPoints.AsNoTracking().Where(p => ids.Contains(p.PlcId))
            .Select(p => new { p.PlcId, p.StationId, p.SignalName, p.ActualAddress, p.Status, p.WriteAllowed }).ToListAsync(ct);
        return conns.Select(c => new PlcConnectionDiag(c.Id, c.Name, c.IpAddress, c.Port, c.Protocol, c.NetworkNumber, c.PcNumber, c.ModuleIoNumber, c.PlcStationNumber, c.MonitoringTimer,
            c.TimeoutMs, c.ConnectTimeoutMs, c.ReadTimeoutMs, c.WriteTimeoutMs, c.PollingIntervalMs, c.XyAddressBase, c.WAddressBase, c.Enabled, c.MissingParameters(),
            stations.Where(s => s.PlcId == c.Id).Select(s =>
            {
                var mine = points.Where(p => p.PlcId == c.Id && p.StationId == s.Id).ToList();
                return new PlcStationDiag(s.Id, s.LocationCode, s.Enabled, mine.Count, mine.Count(p => !string.IsNullOrWhiteSpace(p.ActualAddress)), mine.Count(p => p.Status == IoConfigStatus.Approved),
                    mine.Count(p => p.WriteAllowed), mine.Where(p => p.Status != IoConfigStatus.Approved).Take(10).Select(p => p.SignalName).ToList());
            }).ToList())).ToList();
    }

    public async Task<(IReadOnlyList<PlcLogRow> Rows, int Total)> LogsAsync(string? plcId, DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct)
    {
        var q = db.PlcCommLog.AsNoTracking().Where(x => x.OccurredAt >= from && x.OccurredAt <= to && (plcId == null || x.PlcId == plcId));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(take).ToListAsync(ct);
        rows.Reverse();
        return (rows.Select(x => new PlcLogRow(x.OccurredAt, x.PlcId, x.StationId, x.Level.ToString(), x.Event, x.Message, x.DetailsJson, x.DiagCode, x.Stage)).ToList(), total);
    }

    public async Task<(IReadOnlyList<PlcExchangeRow> Rows, int Total)> ExchangesAsync(string? plcId, DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct)
    {
        var q = db.PlcExchangeLog.AsNoTracking().Where(x => x.OccurredAt >= from && x.OccurredAt <= to && (plcId == null || x.PlcId == plcId));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(take).ToListAsync(ct);
        rows.Reverse();
        return (rows.Select(x => new PlcExchangeRow(x.OccurredAt, x.PlcId, x.Operation, x.Command, x.Device, x.StartAddress, x.Points, x.DurationMs, x.EndCode, x.Outcome, x.Error,
            x.RequestHex, x.ResponseHex, x.IsAmbiguous)).ToList(), total);
    }
}
