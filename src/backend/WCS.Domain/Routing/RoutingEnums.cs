namespace WCS.Domain.Routing;

// CR-010. Enums are stored as readable strings (see WcsDbContext conventions). New members may be appended; existing names never change.

/// <summary>Configuration state of one Business Process version. Only a <see cref="Published"/> version can start a Transport Task.</summary>
public enum ProcessConfigState { Draft = 0, Validated = 1, Published = 2, Disabled = 3 }

/// <summary>
/// Kind of a Route Step. Pickup / Delivery / BufferStorage / BufferRetrieval create a Leg (a movement of one rack from a source to a destination);
/// the kind drives validation (a BufferStorage destination must be a buffer, a BufferRetrieval source must be a buffer, ...). Waiting and Condition do not move a rack.
/// </summary>
public enum RouteStepType { Pickup = 1, Delivery = 2, BufferStorage = 3, BufferRetrieval = 4, Waiting = 5, Condition = 6, ProcessCompletion = 7 }

/// <summary>Where the rack of a movement step comes from. PreviousDestination = the rack left by the previous Leg (for example a buffer).</summary>
public enum SourceMode { Request = 0, Candidates = 1, PreviousDestination = 2 }

/// <summary>Where the rack goes. Request = the location that raised the request.</summary>
public enum DestinationMode { Request = 0, Candidates = 1 }

/// <summary>BeforePick: the destination is chosen when the Leg is created. AfterPick: it is chosen once the rack is on the AMR (continueTask semantics of BP-01/03/06).</summary>
public enum DestinationResolution { BeforePick = 0, AfterPick = 1 }

/// <summary>What happens when no candidate is usable: keep waiting, fail the Transport Task, or continue at the alternative next step.</summary>
public enum WaitBehavior { Wait = 0, Fail = 1, Alternative = 2 }

public enum ErrorBehavior { Fail = 0, Reconcile = 1 }

/// <summary>Kind of request that starts a Business Process. Manual = operator/PDA (not a PLC request).</summary>
public enum RequestType { PickupRequest = 1, DeliveryRequest = 2, ManualRequest = 3 }

public enum CandidateRole { Source = 1, Destination = 2 }

/// <summary>
/// The ONLY conditions the engine can evaluate. Conditions are data (a type plus optional parameters); no script or SQL entered through the UI is ever executed.
/// </summary>
public enum ConditionType
{
    LocationEnabled = 1,
    /// <summary>The location accepts the rack kind and has a free slot (capacity - occupied - reserved).</summary>
    LocationAvailable = 2,
    /// <summary>An unreserved rack of the Transport Task's rack kind is stored at the location.</summary>
    RackPresent = 3,
    /// <summary>Parameter = Full | Empty (or a configured rack type text): the rack at the location matches.</summary>
    RackTypeMatch = 4,
    CapacityAvailable = 5,
    /// <summary>The port of the location exists, is enabled and has no active Station Transaction. A location without a port is always ready.</summary>
    StationReady = 6,
    /// <summary>The PLC serving the port of the location is connected. A location without a port counts as online.</summary>
    PlcOnline = 7,
    /// <summary>Parameter = Empty | Occupied | HasFreeSlot | NotUnknown (WCS inventory view of the location).</summary>
    InventoryState = 8,
    /// <summary>Parameter = Bound | Unbound: whether the rack at the location carries an external rack id (WCS inventory replaces "bind rack" detection, inventory-management.md 4.6).</summary>
    RackBinding = 9,
}

public enum OrderState { Created = 0, Accepted = 1, InProgress = 2, Waiting = 3, Completed = 4, Failed = 5, Cancelled = 6, ReconciliationRequired = 7 }

public enum LegState { Pending = 0, Ready = 1, InProgress = 2, Waiting = 3, Completed = 4, Failed = 5, Cancelled = 6 }

/// <summary>Why a Transport Task / Leg waits. The operator must tell a rack stored at a buffer from an AMR that still carries the rack.</summary>
public enum WaitKind
{
    None = 0,
    /// <summary>No usable destination; the AMR still HOLDS the rack (inventory: IN_TRANSIT, the AGV stays occupied).</summary>
    DestinationUnavailable_AgvHoldingRack = 1,
    /// <summary>The rack is stored at a buffer (inventory updated, the AGV is free); the next step waits for its condition.</summary>
    AtBuffer = 2,
    /// <summary>No usable source rack yet.</summary>
    SourceUnavailable = 3,
    /// <summary>The AGV is busy with another task (PER_AGV) or on dispatch hold.</summary>
    AgvBusy = 4,
    /// <summary>A Waiting step condition is not met yet.</summary>
    ConditionNotMet = 5,
    /// <summary>The leg could not be dispatched (RCS link not approved/available).</summary>
    DispatchBlocked = 6,
    /// <summary>No usable destination yet and no rack has been picked: nothing is on an AMR.</summary>
    DestinationUnavailable = 7,
}

public enum StationOperation { Pickup = 1, Delivery = 2 }

/// <summary>
/// Station Transaction state (CR-010 5.1). The names are business states; the mapping of PLC handshake bits (M9003/M9004/M9005) onto them is
/// NOT decided (open question Q-MAP-1) and is therefore never inferred here.
/// </summary>
public enum StationTransactionState
{
    Created = 0, Requested = 1, Accepted = 2, AgvAssigned = 3, AgvApproaching = 4, AtStation = 5, Handling = 6,
    HandoverCompleted = 7, AgvExited = 8, Completed = 9, Failed = 10, ReconciliationRequired = 11,
}
