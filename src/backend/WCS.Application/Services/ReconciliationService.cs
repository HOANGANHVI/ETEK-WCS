using WCS.Domain.Tasks;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Domain.Inventory;

namespace WCS.Application.Services;

public sealed record ReservationInfoDto(string Kind, string Status, string? LocationCode, Guid? RackId);
public sealed record RackInfoDto(Guid Id, string Status, string? CurrentLocationCode, DateTimeOffset LastConfirmedAt);
public sealed record LocationFlagDto(string Code, bool ReconciliationRequired);

/// <summary>Everything an Admin needs to decide about a task that requires reconciliation (CR-008B-3). Read only.</summary>
public sealed record ReconciliationDetailDto(TaskDto Task, int RcsRetryCount, DateTimeOffset? RcsLastAttemptAt, IReadOnlyList<TaskHistoryDto> RcsEvents,
    IReadOnlyList<ReservationInfoDto> Reservations, RackInfoDto? Rack, IReadOnlyList<LocationFlagDto> Locations, bool CanReconcile);

/// <summary>
/// Admin side of the physical reconciliation. It never releases anything by itself: the release is done only by the existing, audited
/// <c>InventoryService.ReconcileTaskAsync</c> when an Admin states where the rack really is. Recording that the check was inconclusive keeps every resource held.
/// </summary>
public sealed class ReconciliationService(ITransportTaskRepository tasks, IReservationRepository reservations, IRackRepository racks, ILocationRepository locations,
    IUnitOfWork uow, IClock clock)
{
    public const string UnresolvedEvent = "RECONCILIATION_CHECK_UNRESOLVED";

    public async Task<IReadOnlyList<TaskDto>> ListAsync(int take, CancellationToken ct = default) =>
        (await tasks.ListRequiringReconciliationAsync(Math.Clamp(take, 1, 500), ct)).Select(t => t.ToDto()).ToList();

    public async Task<ReconciliationDetailDto> GetDetailAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await tasks.GetAsync(taskId, ct) ?? throw AppException.NotFound("TASK_NOT_FOUND", "Task not found.");
        var rcsEvents = task.History
            .Where(h => h.Source == "RCS" || h.EventName.StartsWith("RCS_", StringComparison.Ordinal) || h.EventName.StartsWith("CANCEL", StringComparison.Ordinal)
                        || h.EventName.StartsWith("RECONCIL", StringComparison.Ordinal))
            .OrderByDescending(h => h.OccurredAt).Take(30).Select(h => h.ToDto()).ToList();

        var locationIds = new HashSet<Guid>();
        var reservationDtos = new List<ReservationInfoDto>();
        foreach (var r in await reservations.ListActiveByTaskAsync(taskId, ct))
        {
            var code = r.LocationId is { } lid ? (await locations.GetAsync(lid, ct))?.Code : null;
            if (r.LocationId is { } l) locationIds.Add(l);
            reservationDtos.Add(new ReservationInfoDto(r.Kind.ToString(), r.Status.ToString(), code, r.RackId));
        }

        RackInfoDto? rackDto = null;
        if (task.RackId is { } rid && await racks.GetAsync(rid, ct) is { } rack)
        {
            var at = rack.CurrentLocationId is { } cl ? await locations.GetAsync(cl, ct) : null;
            rackDto = new RackInfoDto(rack.Id, rack.Status.ToString(), at?.Code, rack.LastConfirmedAt);
        }
        foreach (var id in new[] { task.SourceLocationId, task.DestinationLocationId }) if (id is { } v) locationIds.Add(v);
        var flags = new List<LocationFlagDto>();
        foreach (var id in locationIds)
            if (await locations.GetAsync(id, ct) is { } loc) flags.Add(new LocationFlagDto(loc.Code, loc.ReconciliationRequired));

        return new ReconciliationDetailDto(task.ToDto(), task.RcsRetryCount, task.RcsLastAttemptAt, rcsEvents, reservationDtos, rackDto,
            flags.OrderBy(f => f.Code).ToList(), task.IsTerminal && task.RequiresReconciliation);
    }

    /// <summary>
    /// The Admin checked and could NOT establish where the rack is. Only a history entry is written: the task stays flagged, every reservation and the rack / location
    /// state are kept. (Actor and time are in the history entry; the API adds an audit record.)
    /// </summary>
    public Task RecordUnresolvedCheckAsync(Guid taskId, string note, string actor, CancellationToken ct = default) =>
        uow.ExecuteAsync(async t =>
        {
            var task = await tasks.GetAsync(taskId, t) ?? throw AppException.NotFound("TASK_NOT_FOUND", "Task not found.");
            if (!task.IsTerminal) throw AppException.Rejected("TASK_NOT_FINISHED", "Only finished tasks can be reconciled.");
            if (!task.RequiresReconciliation) throw AppException.Rejected("NO_RECONCILIATION_NEEDED", "Task does not require reconciliation.");
            task.Note(UnresolvedEvent, $"{actor}: {note}", "Operator", clock.UtcNow, HistoryDetail.Of("RECONCILIATION_CHECK_UNRESOLVED", ("actor", actor), ("note", note)));
            return 0;
        }, ct);
}
