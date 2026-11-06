using WCS.Domain.Common;

namespace WCS.Domain.Locations;

public class Location
{
    private Location() { }

    public Location(string code, string name, LocationType type, int rackCapacity, AllowedRackKind allowed,
        bool requiresPlcPermission, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("LOCATION_CODE_REQUIRED", "Location code is required.");
        if (rackCapacity < 0) throw new DomainException("INVALID_CAPACITY", "Capacity cannot be negative.");
        Id = Guid.NewGuid();
        Code = code.Trim();
        Name = name;
        Type = type;
        RackCapacity = rackCapacity;
        AllowedRackKind = allowed;
        RequiresPlcPermission = requiresPlcPermission;
        Enabled = true;
        LastInventoryChangeAt = now;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public LocationType Type { get; private set; }
    /// <summary>Capacity must be configured from reality (inventory-management.md 4.2). NEEDS_CONFIRMATION per location.</summary>
    public int RackCapacity { get; private set; }
    public AllowedRackKind AllowedRackKind { get; private set; }
    public bool Enabled { get; private set; }
    /// <summary>Whether pick/drop here needs PLC permission before the AGV enters (business-rules.md 1.6).</summary>
    public bool RequiresPlcPermission { get; private set; }
    /// <summary>True while the physical state is uncertain (OccupancyStatus UNKNOWN, inventory-management.md 4.8).</summary>
    public bool ReconciliationRequired { get; private set; }
    /// <summary>Touched on every reservation/inventory change so concurrent allocations conflict on <see cref="Version"/>.</summary>
    public DateTimeOffset LastInventoryChangeAt { get; private set; }
    public uint Version { get; private set; }

    public bool Accepts(RackKind kind) => AllowedRackKind switch
    {
        AllowedRackKind.Any => true,
        AllowedRackKind.Full => kind == RackKind.Full,
        AllowedRackKind.Empty => kind == RackKind.Empty,
        _ => false,
    };

    /// <summary>Free slots = capacity - occupied - reserved.</summary>
    public bool HasFreeSlot(int occupied, int reserved) => Enabled && !ReconciliationRequired && occupied + reserved < RackCapacity;

    public OccupancyStatus GetOccupancy(int occupied, int reserved)
    {
        if (ReconciliationRequired) return OccupancyStatus.Unknown;
        if (occupied > 0) return OccupancyStatus.Occupied;
        return reserved > 0 ? OccupancyStatus.Reserved : OccupancyStatus.Empty;
    }

    /// <summary>
    /// Marks the location as changed. The value is guaranteed to differ from the stored one, so EF always issues an UPDATE and the
    /// row-version (xmin) check serializes concurrent allocations of the last free slot even when two calls share the same clock reading.
    /// </summary>
    public void Touch(DateTimeOffset now) =>
        LastInventoryChangeAt = LastInventoryChangeAt is { } last && now <= last ? last.AddTicks(1) : now;

    public void SetReconciliationRequired(bool value, DateTimeOffset now)
    {
        ReconciliationRequired = value; LastInventoryChangeAt = now;
    }

    public void Configure(string name, int capacity, AllowedRackKind allowed, bool enabled, bool requiresPlcPermission, DateTimeOffset now)
    {
        if (capacity < 0) throw new DomainException("INVALID_CAPACITY", "Capacity cannot be negative.");
        Name = name; RackCapacity = capacity; AllowedRackKind = allowed; Enabled = enabled;
        RequiresPlcPermission = requiresPlcPermission; LastInventoryChangeAt = now;
    }
}
