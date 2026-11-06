using System.Net.Sockets;
using System.Text;
using WCS.Application.Plc;
using WCS.Domain.Plc;

namespace WCS.Infrastructure.Plc.Mc;

/// <summary>Raised when a real connection is requested while confirmed parameters are still missing.</summary>
public sealed class NeedsConfigurationException(IReadOnlyList<string> missing)
    : InvalidOperationException("NEEDS_CONFIGURATION: " + string.Join(", ", missing))
{
    public IReadOnlyList<string> Missing { get; } = missing;
}

/// <summary>
/// The Mitsubishi MC Protocol implementation of <see cref="IPlcEndpoint"/>: turns the IO points of a station into the smallest set of
/// batch reads over CONFIRMED, CONTIGUOUS addresses (never reading a gap), decodes BOOL/INT/STRING, and writes single points.
/// </summary>
public sealed class McPlcEndpoint : IPlcEndpoint
{
    private readonly McTcpClient _client;
    public string PlcId { get; }

    public McPlcEndpoint(PlcEndpointConfig c)
    {
        var missing = new List<string>();
        if (c.Port is null) missing.Add("port");
        if (c.Protocol != WCS.Application.Config.ConnectionOptions.PlcMcProtocol3EBinaryTcp) missing.Add("protocol");
        if (c.NetworkNumber is null) missing.Add("networkNumber");
        if (c.PcNumber is null) missing.Add("pcNumber");
        if (c.ModuleIoNumber is null) missing.Add("moduleIoNumber");
        if (c.PlcStationNumber is null) missing.Add("plcStationNumber");
        if (c.MonitoringTimer is null) missing.Add("monitoringTimer");
        if (c.TimeoutMs is null) missing.Add("timeoutMs");
        if (missing.Count > 0) throw new NeedsConfigurationException(missing);
        PlcId = c.PlcId;
        _client = new McTcpClient(new McClientSettings(c.IpAddress, c.Port!.Value,
            new McRoute((byte)c.NetworkNumber!.Value, (byte)c.PcNumber!.Value, (ushort)c.ModuleIoNumber!.Value, (byte)c.PlcStationNumber!.Value),
            (ushort)c.MonitoringTimer!.Value, TimeSpan.FromMilliseconds(c.TimeoutMs!.Value)));
    }

    public EndpointState State => _client.State;
    public PlcDiagnostics Diagnostics => _client.Diagnostics;
    public Task ConnectAsync(CancellationToken ct) => _client.ConnectAsync(ct);
    public Task DisconnectAsync() => _client.DisconnectAsync();
    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private sealed record Item(IoPointRef Point, DeviceAddress Address, int Length);

    public async Task<IReadOnlyList<PointReading>> ReadStationAsync(IReadOnlyList<IoPointRef> points, CancellationToken ct)
    {
        var results = new Dictionary<Guid, PointReading>();
        var items = new List<Item>();
        foreach (var p in points)
        {
            if (string.IsNullOrWhiteSpace(p.Address)) { results[p.Id] = new(p.Id, null, PointQuality.NotConfigured, null); continue; }
            var spec = new PointSpec(p.Id, p.StationId, p.SignalName, p.DataType, p.Address, p.WordLength);
            if (AddressValidator.ValidateOne(spec) is { } issue) { results[p.Id] = new(p.Id, null, PointQuality.Bad, issue.Message); continue; }
            DeviceAddress.TryParse(p.Address, out var a);
            items.Add(new Item(p, a, AddressValidator.Footprint(p.DataType, a, p.WordLength)!.Value));
        }

        var transportFailure = (string?)null;
        foreach (var group in Group(items))
        {
            if (transportFailure is not null) { foreach (var it in group.Items) results[it.Point.Id] = new(it.Point.Id, null, PointQuality.Bad, transportFailure); continue; }
            var first = group.Items[0].Address;
            try
            {
                if (first.Device == PlcDevice.M)
                {
                    var bits = await _client.ReadBitsAsync(first, group.Total, ct);
                    foreach (var it in group.Items) results[it.Point.Id] = new(it.Point.Id, PointValue.OfBool(bits[it.Address.Number - first.Number]), PointQuality.Good, null);
                }
                else
                {
                    var words = await _client.ReadWordsAsync(first, group.Total, ct);
                    foreach (var it in group.Items) results[it.Point.Id] = DecodeWords(it, words.AsSpan(it.Address.Number - first.Number, it.Length));
                }
            }
            catch (McEndCodeException ex)
            {
                foreach (var it in group.Items) results[it.Point.Id] = new(it.Point.Id, null, PointQuality.Bad, $"PLC end code 0x{ex.EndCode:X4}");
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or SocketException or McProtocolException or ObjectDisposedException)
            {
                transportFailure = ex.Message;      // the link is faulted: do not hammer it with the remaining groups
                foreach (var it in group.Items) results[it.Point.Id] = new(it.Point.Id, null, PointQuality.Bad, transportFailure);
            }
        }
        return points.Select(p => results[p.Id]).ToList();
    }

    private sealed record Run(List<Item> Items, int Total);

    /// <summary>Merges only strictly adjacent items of the same device (no gap is ever read) and respects the frame limits.</summary>
    private static IEnumerable<Run> Group(IEnumerable<Item> items)
    {
        foreach (var dev in items.GroupBy(i => i.Address.Device))
        {
            var limit = dev.Key == PlcDevice.M ? McFrame.MaxBitPoints : McFrame.MaxWordPoints;
            Run? run = null; var end = -1;
            foreach (var it in dev.OrderBy(i => i.Address.Number))
            {
                if (run is not null && it.Address.Number == end + 1 && run.Total + it.Length <= limit) { run.Items.Add(it); run = run with { Total = run.Total + it.Length }; /* replaced below */ }
                else { if (run is not null) yield return run; run = new Run([it], it.Length); }
                end = it.Address.Number + it.Length - 1;
            }
            if (run is not null) yield return run;
        }
    }

    private static PointReading DecodeWords(Item it, ReadOnlySpan<ushort> words)
    {
        var p = it.Point;
        switch (p.DataType)
        {
            case PlcDataType.Int: return new(p.Id, PointValue.OfInt((short)words[0]), PointQuality.Good, null);   // signed 16-bit INT (NEEDS_CONFIRMATION: signed vs unsigned)
            case PlcDataType.String:
                if (!string.Equals(p.Encoding, "ASCII", StringComparison.OrdinalIgnoreCase))
                    return new(p.Id, null, PointQuality.Bad, $"Encoding '{p.Encoding}' chưa được hỗ trợ (chỉ ASCII).");
                var bytes = new byte[words.Length * 2];
                for (var i = 0; i < words.Length; i++) { bytes[i * 2] = (byte)(words[i] & 0xFF); bytes[i * 2 + 1] = (byte)(words[i] >> 8); }   // low byte first
                var end = Array.IndexOf(bytes, (byte)0); if (end < 0) end = bytes.Length;
                return new(p.Id, PointValue.OfString(Encoding.ASCII.GetString(bytes, 0, end)), PointQuality.Good, null);
            default: return new(p.Id, null, PointQuality.Bad, "unsupported data type for a word device");
        }
    }

    public async Task WritePointAsync(IoPointRef point, PointValue value, CancellationToken ct)
    {
        if (point.Direction != PlcSignalDirection.SoftwareToPlc) throw new InvalidOperationException("only Software -> PLC points can be written");
        if (!DeviceAddress.TryParse(point.Address, out var a)) throw new InvalidOperationException("point has no confirmed address");
        if (AddressValidator.ValidateOne(new PointSpec(point.Id, point.StationId, point.SignalName, point.DataType, point.Address, point.WordLength)) is { } issue)
            throw new InvalidOperationException(issue.Message);
        if (value.Type != point.DataType) throw new ArgumentException($"value type {value.Type} does not match {point.DataType}");
        switch (point.DataType)
        {
            case PlcDataType.Bool: await _client.WriteBitsAsync(a, [(bool)value.Value], ct); break;
            case PlcDataType.Int:
                var i = (int)value.Value;
                if (i is < short.MinValue or > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(value), "INT must fit 16 bits");
                await _client.WriteWordsAsync(a, [unchecked((ushort)(short)i)], ct); break;
            case PlcDataType.String:
                if (!string.Equals(point.Encoding, "ASCII", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("only ASCII STRING is supported");
                var text = Encoding.ASCII.GetBytes((string)value.Value);
                var capacity = point.WordLength!.Value * 2;
                if (text.Length > capacity) throw new ArgumentOutOfRangeException(nameof(value), $"STRING longer than {capacity} bytes");
                var buf = new byte[capacity]; text.CopyTo(buf, 0);
                var words = new ushort[point.WordLength.Value];
                for (var w = 0; w < words.Length; w++) words[w] = (ushort)(buf[w * 2] | (buf[w * 2 + 1] << 8));
                await _client.WriteWordsAsync(a, words, ct); break;
        }
    }
}
