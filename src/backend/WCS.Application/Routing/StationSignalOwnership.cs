using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Plc;

namespace WCS.Application.Routing;

/// <summary>
/// CR-042 (OQ-42-03): the task engine side of "one signal, one engine". It refuses a request of a station assigned elsewhere, and a Request_ID of a station that the transport order
/// engine has already received (a cut-over between two reads).
/// </summary>
public sealed class StationSignalOwnership(IStationEngineRouter engines, IRoutingRepository routing, IUnitOfWork uow) : IStationSignalOwnership
{
    public async Task EnsureTaskEngineAsync(string station, string requestKey, CancellationToken ct = default)
    {
        if (engines.EngineFor(station) is StationEngine.TransportOrder or StationEngine.Invalid)
            throw AppException.Rejected(StationEngineCodes.IsOrder, $"Station {station} is not assigned to the Phase 1 task engine.");
        var parts = requestKey.Split(':', 3);   // PLC keys are PlcId:Station:Request_ID
        if (parts.Length == 3 && await uow.ExecuteAsync(async t => await routing.GetTransactionByRequestIdAsync(parts[0], parts[2], t), ct) is { } other && other.PortId == parts[1])
            throw AppException.Rejected(StationEngineCodes.TakenByOther, "The transport order engine already received this Request_ID of this station.");
    }
}
