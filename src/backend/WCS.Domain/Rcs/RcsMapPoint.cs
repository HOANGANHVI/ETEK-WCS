namespace WCS.Domain.Rcs;

/// <summary>
/// CR-066: what WCS adds to a point of an RCS map: a display name and the WCS location it stands for. The imported XML is never changed.
/// The record belongs to a MapID and a point id and remembers where that point was: after a new import it counts only while the point is still there.
/// </summary>
public class RcsMapPoint
{
    private RcsMapPoint() { }
    public RcsMapPoint(string mapId, long pointId) { MapId = mapId; PointId = pointId; }

    public string MapId { get; private set; } = "";
    public long PointId { get; private set; }
    public double X { get; private set; }
    public double Y { get; private set; }
    public string? Name { get; private set; }
    public Guid? LocationId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public void Set(double x, double y, string? name, Guid? locationId, string? by, DateTimeOffset now)
    {
        X = x; Y = y; Name = name; LocationId = locationId; UpdatedBy = by; UpdatedAt = now;
    }
}
