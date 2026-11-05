namespace WCS.Domain.Common;

/// <summary>Business role of an AGV (business-rules.md 1.2). AGV1 carries full racks, AGV2 carries empty racks.</summary>
public enum AgvRole { FullRack = 1, EmptyRack = 2 }

/// <summary>Whether a task moves a full or an empty rack. Determines the AGV role.</summary>
public enum RackKind { Full = 1, Empty = 2 }

public enum AgvConnectionStatus { Unknown = 0, Online = 1, Offline = 2, Error = 3 }

public enum LocationType { Station = 1, Buffer = 2, ManualBuffer = 3, Waypoint = 4 }

/// <summary>Which rack kind a location may hold (inventory-management.md 4.2 AllowedRackStatus).</summary>
public enum AllowedRackKind { Any = 0, Full = 1, Empty = 2 }

/// <summary>inventory-management.md 4.2: FULL, EMPTY, UNKNOWN, IN_TRANSIT.</summary>
public enum RackStatus { Unknown = 0, Full = 1, Empty = 2, InTransit = 3 }

/// <summary>inventory-management.md 4.2: EMPTY, OCCUPIED, UNKNOWN, RESERVED (derived view).</summary>
public enum OccupancyStatus { Empty = 0, Occupied = 1, Unknown = 2, Reserved = 3 }

/// <summary>
/// Approved process flows. BP-04 (slide 8) is deliberately ABSENT: it is REVIEW and out of scope (BR-02).
/// A3/A4 and D1/D2 are UNCHANGED legacy processes and are not modelled here (BR-04).
/// </summary>
public enum ProcessFlow { Bp01 = 1, Bp02 = 2, Bp03 = 3, Bp05 = 5, Bp06 = 6 }

public enum InventoryEventType
{
    PickConfirmed = 1,
    DropConfirmed = 2,
    ManualAdjustment = 3,
    InitialStockDeclared = 4,
    Reconciliation = 5,
}

public enum ConfirmationSource { Plc = 1, Rcs = 2, Operator = 3, WcsReconciliation = 4, Simulator = 5 }

public enum BindOutcome { NotRequested = 0, Bound = 1, NotBoundByProcess = 2 }

public enum ReservationKind { SourceRack = 1, DestinationLocation = 2 }

public enum ReservationStatus { Active = 1, Consumed = 2, Released = 3 }

public enum TaskLeg { Pick = 1, Drop = 2 }
