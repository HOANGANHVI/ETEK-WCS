using System.Linq.Expressions;
using WCS.Domain.Inventory;
using WCS.Domain.Routing;

namespace WCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// CR-042 (OQ-42-08): the definition of "a transport order owns this rack", derived from the order and its legs. An unfinished order (any state except Completed, Cancelled and Failed,
/// so also Waiting, Held and ReconciliationRequired) owns the racks it has picked and the racks of its open legs, wherever they stand (a buffer included). A Failed order keeps owning
/// the racks it has picked while <see cref="TransportOrder.RacksHeld"/> is set, until an Admin releases them. The two expressions below are the same rule and must change together.
/// </summary>
internal static class RackOwnership
{
    public static Expression<Func<TransportOrder, bool>> Owns(Guid rackId) => o =>
        (o.State != OrderState.Completed && o.State != OrderState.Failed && o.State != OrderState.Cancelled
         && o.Legs.Any(l => l.RackId == rackId && (l.PickConfirmedAt != null || (l.State != LegState.Completed && l.State != LegState.Failed && l.State != LegState.Cancelled))))
        || (o.RacksHeld && o.Legs.Any(l => l.RackId == rackId && l.PickConfirmedAt != null));

    /// <summary>Racks that no order owns, apart from <paramref name="orderId"/> itself.</summary>
    public static Expression<Func<Rack, bool>> NotOwnedByAnother(WcsDbContext db, Guid? orderId)
    {
        var except = orderId ?? Guid.Empty;
        return r => !db.TransportOrders.Any(o => o.Id != except &&
            ((o.State != OrderState.Completed && o.State != OrderState.Failed && o.State != OrderState.Cancelled
              && o.Legs.Any(l => l.RackId == r.Id && (l.PickConfirmedAt != null || (l.State != LegState.Completed && l.State != LegState.Failed && l.State != LegState.Cancelled))))
             || (o.RacksHeld && o.Legs.Any(l => l.RackId == r.Id && l.PickConfirmedAt != null))));
    }
}
