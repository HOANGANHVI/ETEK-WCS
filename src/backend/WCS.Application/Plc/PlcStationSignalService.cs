using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Domain.Plc;

namespace WCS.Application.Plc;

/// <summary>
/// The PLC-backed <see cref="IStationSignalService"/>. It plugs the station IO into the existing task flow WITHOUT changing the flow:
/// it only turns a rising edge of an approved "station + request signal" mapping into a <see cref="StationRequest"/>.
/// Everything the workbook leaves open stays open:
///  - which flow a Pickup_Request / Delivery_Request means: only the explicit approved mappings in Plc:Integration:StationFlows (empty by default);
///  - the Request_ID encoding: the string is used as read (no parsing) to form the idempotency key, and a request without a readable ID is skipped;
///  - Request_Ack and Transfer_Permit conditions: not driven here (NEEDS_CONFIRMATION); only approved handshake rules may write.
/// Stations without a mapping (all of them by default) are monitor-only and can never create a task.
/// </summary>
public sealed class PlcStationSignalService(IPlcRuntime runtime, IOptions<PlcRuntimeOptions> options, IPlcEvents events) : IStationSignalService
{
    private readonly ConcurrentDictionary<(string Station, string Signal), bool> _high = new();
    private readonly ConcurrentDictionary<string, bool> _warned = new();

    public Task<IReadOnlyList<StationRequest>> PollRequestsAsync(CancellationToken ct = default)
    {
        var result = new List<StationRequest>();
        if (!options.Value.Integration.Enabled) return Task.FromResult<IReadOnlyList<StationRequest>>(result);
        foreach (var map in options.Value.Integration.StationFlows)
        {
            if (map.Signal is not ("Pickup_Request" or "Delivery_Request") || string.IsNullOrWhiteSpace(map.Flow)) continue;
            var handler = runtime.Handler(map.Station);
            if (handler is null) continue;
            var bit = handler.Read(map.Signal);
            var high = bit is { Quality: PointQuality.Good or PointQuality.Simulated, Value: { Type: PlcDataType.Bool } v } && (bool)v.Value;
            var key = (map.Station, map.Signal);
            var wasHigh = _high.GetValueOrDefault(key);
            if (bit is { Quality: PointQuality.Good or PointQuality.Simulated }) _high[key] = high;     // data that is not live never creates an edge
            if (!high || wasHigh) continue;                                                              // act on the rising edge only

            var id = handler.Read("Request_ID");
            if (id is not { Quality: PointQuality.Good or PointQuality.Simulated, Value: { Type: PlcDataType.String } s } || string.IsNullOrWhiteSpace((string)s.Value))
            {
                if (_warned.TryAdd($"{map.Station}:{map.Signal}", true))
                    events.Log(PlcLogLevel.Warning, handler.PlcId, map.Station, "REQUEST_ID_MISSING", $"{map.Signal} bật nhưng Request_ID chưa đọc được (NEEDS_CONFIRMATION: địa chỉ/encoding). Không tạo nhiệm vụ.");
                _high[key] = false;                                                                      // look again on the next cycle instead of losing the request
                continue;
            }
            _warned.TryRemove($"{map.Station}:{map.Signal}", out _);
            result.Add(new StationRequest(map.Station, map.Flow, $"{handler.PlcId}:{map.Station}:{((string)s.Value).Trim()}"));
            events.Log(PlcLogLevel.Info, handler.PlcId, map.Station, "REQUEST", $"{map.Signal} -> {map.Flow}, Request_ID {((string)s.Value).Trim()}");
        }
        return Task.FromResult<IReadOnlyList<StationRequest>>(result);
    }

    public Task AcknowledgeRequestAsync(StationRequest request, CancellationToken ct = default)
    {
        // Writing Request_Ack is a handshake rule (NEEDS_CONFIRMATION). Without an approved rule nothing is written.
        events.Log(PlcLogLevel.Info, null, request.StationCode, "REQUEST_HANDLED", $"{request.RequestKey} đã được WCS ghi nhận (Request_Ack không tự ghi: chưa có quy tắc handshake được duyệt).");
        return Task.CompletedTask;
    }

    public Task RequestTransferPermissionAsync(string stationCode, TransferDirection direction, Guid taskId, CancellationToken ct = default)
    {
        // Setting Transfer_Permit is a motion-related control signal: its conditions are NOT approved, so it is never written here.
        events.Log(PlcLogLevel.Warning, null, stationCode, "TRANSFER_PERMIT_NOT_APPROVED",
            $"Nhiệm vụ {taskId} xin phép {direction} tại {stationCode}: điều kiện bật Transfer_Permit chưa được phê duyệt (NEEDS_CONFIRMATION), không ghi tín hiệu.");
        return Task.CompletedTask;
    }
}
