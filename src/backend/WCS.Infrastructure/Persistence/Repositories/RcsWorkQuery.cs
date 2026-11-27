using Microsoft.EntityFrameworkCore;
using WCS.Application.Abstractions;
using WCS.Domain.Rcs;

namespace WCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// CR-051: the RCS work of both engines as one list, read from the records that own it (transport_tasks, transport_legs / transport_orders, rcs_messages). Nothing is copied or written;
/// no second task is created for anything.
/// </summary>
internal sealed class RcsWorkQuery(WcsDbContext db) : IRcsWorkQuery
{
    private const int Candidates = 500;

    public async Task<IReadOnlyList<RcsWorkItem>> ListAsync(bool sentOnly, Guid? orderId, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var tasks = orderId is null ? await db.Tasks.AsNoTracking().OrderByDescending(t => t.UpdatedAt).Take(Candidates).ToListAsync(ct) : [];
        var legQuery = from l in db.TransportLegs.AsNoTracking()
                       join o in db.TransportOrders.AsNoTracking() on l.OrderId equals o.Id
                       where orderId == null || o.Id == orderId
                       orderby l.UpdatedAt descending
                       select new { Leg = l, o.RequestKey, o.ProcessCode, o.RequestedBy, o.ExecutionMode };
        var legs = await legQuery.Take(Candidates).ToListAsync(ct);

        var codes = tasks.Select(t => t.RcsRequestCode).Concat(legs.Select(x => x.Leg.AgvRequestCode)).Distinct().ToList();
        // only what tells where the work stands: the creation of the RCS task, its answer, refusals and the AMR events (status polling is not needed)
        var rows = codes.Count == 0 ? [] : await db.RcsMessages.AsNoTracking()
            .Where(m => m.RequestCode != null && codes.Contains(m.RequestCode) && !m.MessageType.StartsWith("Query"))
            .Select(m => new { m.RequestCode, m.Seq, m.CreatedAt, m.MessageType, m.Direction, m.Result, m.ErrorMessage }).ToListAsync(ct);
        var messages = rows.ToLookup(m => m.RequestCode!, m => new RcsWorkMessage(m.Seq, m.CreatedAt, m.MessageType, m.Direction == RcsMessageDirection.Out, m.Result, m.ErrorMessage));

        var agvs = await db.Agvs.AsNoTracking().ToDictionaryAsync(a => a.Id, a => new { a.Code, a.RcsRobotCode }, ct);
        var places = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Code, ct);
        string? Place(Guid? id) => id is { } x && places.TryGetValue(x, out var c) ? c : null;

        var items = new List<RcsWorkItem>();
        foreach (var t in tasks)
        {
            var m = messages[t.RcsRequestCode].ToList();
            var s = RcsWorkRules.Derive(t.State.ToString(), t.RcsTaskCode, t.PickConfirmedAt is not null, m);
            var agv = t.AgvId is { } id && agvs.TryGetValue(id, out var a) ? a : null;
            items.Add(new("Task", t.Id, null, null, t.RequestKey, t.Flow.ToString(), t.RequestedBy, t.ExecutionMode?.ToString(), t.AgvId, agv?.Code, t.RobotCode ?? agv?.RcsRobotCode,
                Place(t.SourceLocationId ?? t.RequestLocationId), Place(t.DestinationLocationId), t.State.ToString(), s.State, t.RcsRequestCode, t.RcsTaskCode, s.Problem ?? t.StateReason,
                t.CreatedAt, s.SentAt, Latest(t.UpdatedAt, s.LastMessageAt), t.PickConfirmedAt, t.DropConfirmedAt, t.FinishedAt));
        }
        foreach (var x in legs)
        {
            var l = x.Leg; var m = messages[l.AgvRequestCode].ToList();
            var s = RcsWorkRules.Derive(l.State.ToString(), l.AgvTaskCode, l.PickConfirmedAt is not null, m);
            var agv = l.AgvId is { } id && agvs.TryGetValue(id, out var a) ? a : null;
            items.Add(new("OrderLeg", l.Id, l.OrderId, l.Sequence, x.RequestKey, x.ProcessCode, x.RequestedBy, x.ExecutionMode?.ToString(), l.AgvId, agv?.Code, l.RobotCode ?? agv?.RcsRobotCode,
                l.SourceLocationCode, l.DestinationLocationCode, l.State.ToString(), s.State, l.AgvRequestCode, l.AgvTaskCode, s.Problem ?? l.StateReason,
                l.CreatedAt, s.SentAt, Latest(l.UpdatedAt, s.LastMessageAt), l.PickConfirmedAt, l.DropConfirmedAt, l.FinishedAt));
        }
        return items.Where(i => !sentOnly || i.RcsState != RcsWorkStates.NotSent)
            .OrderByDescending(i => i.SentAt ?? i.CreatedAt).ThenByDescending(i => i.UpdatedAt).Take(take).ToList();
    }

    private static DateTimeOffset Latest(DateTimeOffset entity, DateTimeOffset? message) => message is { } m && m > entity ? m : entity;
}
