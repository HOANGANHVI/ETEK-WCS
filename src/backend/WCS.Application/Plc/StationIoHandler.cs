using WCS.Domain.Plc;

namespace WCS.Application.Plc;

/// <summary>One IO point as shown to the UI. Address and error text are filled only for Admin snapshots.</summary>
public sealed record IoPointState(Guid Id, string PlcId, string StationId, int Seq, string Group, string SignalName, string Direction, string DataType,
    string ConfigStatus, object? Value, string Quality, DateTimeOffset? LastUpdated, string? Address, string? Error);

public sealed record StationSnapshot(string PlcId, string StationId, int Total, int Configured, int Good, IReadOnlyList<IoPointState> Points);

public sealed record PointInfo(IoPointRef Ref, int Seq, IoConfigStatus Status);

/// <summary>
/// The reusable per-Station IO handler (one instance per Station): holds the current value, quality and timestamp of the 23 points and
/// nothing else. It never touches another station's data; thread-safe because the worker writes while the API reads.
/// </summary>
public sealed class StationIoHandler
{
    private sealed class Entry(PointInfo info)
    {
        public PointInfo Info { get; } = info;
        public PointValue? Value { get; set; }
        public PointQuality Quality { get; set; } = info.Ref.Address is null ? PointQuality.NotConfigured : PointQuality.Unknown;
        public DateTimeOffset? Updated { get; set; }
        public string? Error { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _entries;
    private readonly List<Guid> _order;

    public string PlcId { get; }
    public string StationId { get; }

    public StationIoHandler(string plcId, string stationId, IEnumerable<PointInfo> points)
    {
        PlcId = plcId; StationId = stationId;
        var list = points.OrderBy(p => p.Seq).ToList();
        if (list.Any(p => p.Ref.PlcId != plcId || p.Ref.StationId != stationId))
            throw new ArgumentException("a station handler can only hold the points of its own PLC and station");
        _entries = list.ToDictionary(p => p.Ref.Id, p => new Entry(p));
        _order = list.Select(p => p.Ref.Id).ToList();
    }

    public IReadOnlyList<IoPointRef> Points { get { lock (_gate) return _order.Select(id => _entries[id].Info.Ref).ToList(); } }

    /// <summary>Applies one read cycle. Returns the ids of the points whose value, quality or error changed.</summary>
    public IReadOnlyList<Guid> Apply(IReadOnlyList<PointReading> readings, DateTimeOffset now, bool simulated)
    {
        var changed = new List<Guid>();
        lock (_gate)
        {
            foreach (var r in readings)
            {
                if (!_entries.TryGetValue(r.PointId, out var e)) continue;      // not ours: ignored (no cross-station writes)
                var quality = r.Quality == PointQuality.Good && simulated ? PointQuality.Simulated : r.Quality;
                var value = quality is PointQuality.Good or PointQuality.Simulated ? r.Value : (quality == PointQuality.NotConfigured ? null : e.Value);   // keep last known value on errors
                var different = !Equals(e.Value, value) || e.Quality != quality || e.Error != r.Error;
                e.Value = value; e.Quality = quality; e.Error = r.Error;
                if (quality is PointQuality.Good or PointQuality.Simulated) e.Updated = now;
                if (different) changed.Add(e.Info.Ref.Id);
            }
        }
        return changed;
    }

    /// <summary>The PLC link is down: the last values stay visible but their quality says they are not live.</summary>
    public IReadOnlyList<Guid> MarkDisconnected(string? reason)
    {
        var changed = new List<Guid>();
        lock (_gate)
            foreach (var e in _entries.Values)
            {
                if (e.Quality == PointQuality.NotConfigured || (e.Quality == PointQuality.Disconnected && e.Error == reason)) continue;
                e.Quality = PointQuality.Disconnected; e.Error = reason; changed.Add(e.Info.Ref.Id);
            }
        return changed;
    }

    public StationSnapshot Snapshot(bool admin, IReadOnlyCollection<Guid>? only = null)
    {
        lock (_gate)
        {
            var all = _order.Select(id => _entries[id]).ToList();
            var points = all.Where(e => only is null || only.Contains(e.Info.Ref.Id)).Select(e => new IoPointState(e.Info.Ref.Id, PlcId, StationId, e.Info.Seq, e.Info.Ref.Group,
                e.Info.Ref.SignalName, e.Info.Ref.Direction.ToString(), e.Info.Ref.DataType.ToString(), PlcLabels.Status(e.Info.Status), e.Value?.Value, e.Quality.ToString(),
                e.Updated, admin ? e.Info.Ref.Address : null, admin ? e.Error : null)).ToList();
            return new StationSnapshot(PlcId, StationId, all.Count, all.Count(e => e.Quality != PointQuality.NotConfigured),
                all.Count(e => e.Quality is PointQuality.Good or PointQuality.Simulated), points);
        }
    }

    /// <summary>Current value of a signal (used by the handshake framework and the integration; never an address).</summary>
    public (PointValue? Value, PointQuality Quality)? Read(string signalName)
    {
        lock (_gate)
        {
            var e = _entries.Values.FirstOrDefault(x => x.Info.Ref.SignalName == signalName);
            return e is null ? null : (e.Value, e.Quality);
        }
    }

    public IoPointRef? Find(string signalName)
    {
        lock (_gate) return _entries.Values.FirstOrDefault(x => x.Info.Ref.SignalName == signalName)?.Info.Ref;
    }
}
