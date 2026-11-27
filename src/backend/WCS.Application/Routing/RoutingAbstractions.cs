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

    /// <summary>CR-035 (D-11/D-12): when true a request is only accepted for a Published process version that is also Ready. Default false = behaviour unchanged (feature flag; existing deployments keep working).</summary>
    public bool EnforceReadiness { get; set; }

    /// <summary>
    /// CR-040 (D-12): every version published from now on accepts a request only when Ready, and its requests go through the queue. Default true. Versions published before keep
    /// their behaviour (the flag is stored per process at publication).
    /// </summary>
    public bool NewPublicationsRequireReadiness { get; set; } = true;

    /// <summary>CR-042 (OQ-42-04): minutes after which the oldest waiting request of a queue scope is reported as stalled. Nothing is cancelled or overtaken. 0 = no warning.</summary>
    public int QueueStallWarningMinutes { get; set; } = 10;

    /// <summary>CR-046 (F2): the first leg of an order reserves no destination while its AGV has a leg without one (Legacy dispatch). false = the behaviour before CR-046.</summary>
    public bool DeferDestinationWhileAgvNeedsOne { get; set; } = true;
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
    /// <summary>CR-036 (A.1): true when a Published version uses the location as trigger, candidate, waiting position or in a condition (the condition test is a conservative text match).</summary>
    Task<bool> IsLocationReferencedByPublishedAsync(string locationCode, CancellationToken ct = default);

    // ---- orders and legs
    Task<TransportOrder?> GetOrderAsync(Guid id, CancellationToken ct = default);   // includes legs
    Task<TransportOrder?> GetOrderByRequestKeyAsync(string requestKey, CancellationToken ct = default);
    void Add(TransportOrder order);
    void Add(TransportLeg leg);
    Task<TransportLeg?> GetLegAsync(Guid id, CancellationToken ct = default);
    Task<TransportLeg?> GetActiveLegForAgvAsync(Guid agvId, CancellationToken ct = default);
    /// <summary>CR-046 (F1): serialises, until the transaction ends, every decision that gives an AGV a job (Phase 1 task or leg).</summary>
    Task LockAgvAsync(Guid agvId, CancellationToken ct = default);
    /// <summary>CR-046 (F2): a leg of this rack kind (so of the same AGV in Legacy dispatch), pending or running, that has no destination yet.</summary>
    Task<bool> HasLegWithoutDestinationAsync(WCS.Domain.Common.RackKind kind, CancellationToken ct = default);
    /// <summary>CR-028: legs that are on their way (an AGV chosen by WCS, or sent to the RCS) and not finished. Legacy legs carry an AgvId; RcsManaged legs are sent (task code / in progress) with no AgvId.</summary>
    Task<int> CountInFlightLegsByModeAsync(bool rcsManaged, CancellationToken ct = default);
    /// <summary>CR-028: legs of RcsManaged dispatch that were sent to the RCS (task code set, no AgvId) and are not finished: their AMR is asked from the RCS.</summary>
    Task<IReadOnlyList<Guid>> ListManagedLegIdsToSyncAsync(int take, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListOpenOrderIdsAsync(int take, CancellationToken ct = default);
    /// <summary>CR-040: serializes queue decisions of one key until the surrounding transaction ends.</summary>
    Task LockQueueAsync(string key, CancellationToken ct = default);
    /// <summary>CR-040: the next value of the database queue sequence.</summary>
    Task<long> NextQueueSequenceAsync(CancellationToken ct = default);
    /// <summary>CR-040: the oldest open request of the scope, older than <paramref name="sequence"/>, that still has to be given a resource (not dispatched yet, between two legs, or a leg without destination).</summary>
    Task<TransportOrder?> FindOlderWaitingInScopeAsync(string scope, long sequence, CancellationToken ct = default);
    /// <summary>CR-040: true when an older request of the scope still has a leg that was not sent to the AGV system.</summary>
    Task<bool> HasOlderUnsentLegInScopeAsync(string scope, long sequence, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListReadyLegIdsAsync(int take, CancellationToken ct = default);
    Task<(IReadOnlyList<TransportOrder> Items, int Total)> QueryOrdersAsync(OrderState? state, int skip, int take, CancellationToken ct = default);
    void Add(RoutingEvent e);
    /// <summary>CR-042: the newest event of this kind recorded for a leg.</summary>
    Task<RoutingEvent?> GetLastLegEventAsync(Guid legId, string kind, CancellationToken ct = default);
    /// <summary>CR-042: true when the order already has an event of this kind recorded at or after the given time.</summary>
    Task<bool> HasOrderEventSinceAsync(Guid orderId, string kind, DateTimeOffset since, CancellationToken ct = default);
    /// <summary>CR-042: every queued order that is Waiting, in queue order.</summary>
    Task<IReadOnlyList<TransportOrder>> ListWaitingQueuedOrdersAsync(int take, CancellationToken ct = default);
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
    /// <summary>CR-040: enabled ports that are linked to a location.</summary>
    Task<IReadOnlyList<PlcStation>> ListLinkedPortsAsync(CancellationToken ct = default);
    /// <summary>CR-040: the PLC → WCS points of a station, as the IO configuration declares them (read only).</summary>
    Task<IReadOnlyList<StationIoPoint>> ListPlcInputPointsAsync(string stationId, CancellationToken ct = default);
    /// <summary>CR-040: the request key of an open transport order that uses the rack (null = none).</summary>
    Task<string?> FindOrderOwningRackAsync(Guid rackId, CancellationToken ct = default);
}

/// <summary>Whether the link to a PLC is currently up (read from the PLC runtime). Null = unknown.</summary>
public interface IPlcLinkStatus
{
    bool? IsOnline(string plcId);
}

public enum LegDispatchOutcome { Accepted = 1, Blocked = 2, Rejected = 3, Unknown = 4 }

public sealed record LegDispatchRequest(Guid LegId, string RequestCode, string? RobotCode, RackKind RackKind, string SourceLocationCode, string? DestinationLocationCode, DateTimeOffset? OrderCreatedAt = null);

public sealed record LegDispatchResult(LegDispatchOutcome Outcome, string? AgvTaskCode, string? Reason);

/// <summary>
/// Sends one Leg to the AGV fleet. The only implementation today drives the simulator: the RCS task template for configurable legs is not confirmed
/// (open question Q-MLEG-1), so a real adapter is deliberately not provided. Business logic depends only on this contract.
/// </summary>
public interface ILegDispatcher
{
    Task<LegDispatchResult> DispatchAsync(LegDispatchRequest request, CancellationToken ct = default);
    Task<LegDispatchResult> ContinueAsync(string requestCode, string? agvTaskCode, string destinationLocationCode, CancellationToken ct = default);
    /// <summary>CR-028: asks the RCS which AMR carries the leg (queryTaskStatus, `agvCode` "exists when the task has been assigned"). Default: inconclusive (an adapter that cannot ask reports nothing).</summary>
    Task<WCS.Application.Abstractions.RcsTaskInfo> QueryAsync(string requestCode, CancellationToken ct = default) =>
        Task.FromResult(new WCS.Application.Abstractions.RcsTaskInfo(WCS.Application.Abstractions.RcsTaskStatus.Unknown, null, null));
}
