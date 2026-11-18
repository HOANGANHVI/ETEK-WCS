using WCS.Domain.Common;
using WCS.Domain.Plc;
using WCS.Domain.Routing;

namespace WCS.Application.Routing;

/// <summary>
/// CR-010 configuration. Nothing here is a route: routes live in the database. These are technical switches and the lists that protect approved behaviour.
/// </summary>
public sealed class RouteEngineOptions
{
    public const string SectionName = "RouteEngine";

    /// <summary>Locations whose process is UNCHANGED (A3/A4, D1/D2 in the approved baseline). A process may not use them: validation refuses it. Data, not code.</summary>
    public string[] ProtectedLocationCodes { get; set; } = [];

    /// <summary>PLC register value -> location code for Rack_Full / Rack_Empty (D1510 / D1512). Confirmed values only: 1..4 = A1..A4. Never extended automatically.</summary>
    public Dictionary<string, string> PlcLocationCodes { get; set; } = new();
}

/// <summary>Persistence of the routing aggregates. Implemented in Infrastructure; every method takes part in the current unit of work.</summary>
public interface IRoutingRepository
{
    // ---- processes
    Task<BusinessProcess?> GetProcessAsync(Guid id, CancellationToken ct = default);
    Task<BusinessProcess?> GetProcessByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessProcess>> ListProcessesAsync(CancellationToken ct = default);
    void Add(BusinessProcess process);
    Task<BusinessProcessVersion?> GetVersionAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessProcessVersion>> ListVersionsAsync(Guid processId, CancellationToken ct = default);
    Task<BusinessProcessVersion?> GetPublishedVersionAsync(Guid processId, CancellationToken ct = default);
    /// <summary>Enabled processes with a Published version whose request type / rack kind / trigger location match. Used to select the process of a request.</summary>
    Task<IReadOnlyList<(BusinessProcess Process, BusinessProcessVersion Version)>> FindPublishedAsync(RequestType type, RackKind? kind, string locationCode, CancellationToken ct = default);
    void Add(BusinessProcessVersion version);

    // ---- orders and legs
    Task<TransportOrder?> GetOrderAsync(Guid id, CancellationToken ct = default);   // includes legs
    Task<TransportOrder?> GetOrderByRequestKeyAsync(string requestKey, CancellationToken ct = default);
    void Add(TransportOrder order);
    void Add(TransportLeg leg);
    Task<TransportLeg?> GetLegAsync(Guid id, CancellationToken ct = default);
    Task<TransportLeg?> GetActiveLegForAgvAsync(Guid agvId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListOpenOrderIdsAsync(int take, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListReadyLegIdsAsync(int take, CancellationToken ct = default);
    Task<(IReadOnlyList<TransportOrder> Items, int Total)> QueryOrdersAsync(OrderState? state, int skip, int take, CancellationToken ct = default);
    void Add(RoutingEvent e);
    /// <summary>Writes pending changes inside the current unit of work (needed when two updates must reach the database in a fixed order).</summary>
    Task FlushAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RoutingEvent>> ListEventsAsync(Guid orderId, int take, CancellationToken ct = default);

    // ---- station transactions
    Task<StationTransaction?> GetTransactionAsync(Guid id, CancellationToken ct = default);
    Task<StationTransaction?> GetActiveTransactionForPortAsync(string portId, CancellationToken ct = default);
    Task<StationTransaction?> GetTransactionByRequestIdAsync(string plcId, string requestId, CancellationToken ct = default);
    Task<IReadOnlyList<StationTransaction>> ListTransactionsAsync(bool activeOnly, string? plcId, Guid? orderId, int take, CancellationToken ct = default);
    void Add(StationTransaction tx);

    // ---- ports (PLC stations linked to a location)
    Task<PlcStation?> FindPortByLocationAsync(string locationCode, CancellationToken ct = default);
    Task<PlcStation?> GetPortAsync(string portId, CancellationToken ct = default);
    /// <summary>True when the port has IO points and every one of them is Approved by the PLC engineer. Physical operations are refused otherwise.</summary>
    Task<bool> IsPortMappingApprovedAsync(string portId, CancellationToken ct = default);
    Task<bool> IsPlcEnabledAsync(string plcId, CancellationToken ct = default);
}

/// <summary>Whether the link to a PLC is currently up (read from the PLC runtime). Null = unknown.</summary>
public interface IPlcLinkStatus
{
    bool? IsOnline(string plcId);
}

public enum LegDispatchOutcome { Accepted = 1, Blocked = 2, Rejected = 3, Unknown = 4 }

public sealed record LegDispatchRequest(Guid LegId, string RequestCode, string? RobotCode, RackKind RackKind, string SourceLocationCode, string? DestinationLocationCode);

public sealed record LegDispatchResult(LegDispatchOutcome Outcome, string? AgvTaskCode, string? Reason);

/// <summary>
/// Sends one Leg to the AGV fleet. The only implementation today drives the simulator: the RCS task template for configurable legs is not confirmed
/// (open question Q-MLEG-1), so a real adapter is deliberately not provided. Business logic depends only on this contract.
/// </summary>
public interface ILegDispatcher
{
    Task<LegDispatchResult> DispatchAsync(LegDispatchRequest request, CancellationToken ct = default);
    Task<LegDispatchResult> ContinueAsync(string requestCode, string? agvTaskCode, string destinationLocationCode, CancellationToken ct = default);
}
