using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Domain.Tasks;

namespace WCS.Application.Services;

/// <summary>Read side used by the API. Pure reads; never mutates state.</summary>
public sealed class WcsQueries(
    IAgvRepository agvs,
    ILocationRepository locations,
    IRackRepository racks,
    IReservationRepository reservations,
    IInventoryTransactionRepository transactions,
    ITransportTaskRepository tasks)
{
    public async Task<IReadOnlyList<AgvDto>> ListAgvsAsync(CancellationToken ct = default)
    {
        var active = await tasks.ActiveTaskIdByAgvAsync(ct);
        return (await agvs.ListAsync(ct)).Select(a => a.ToDto(active.TryGetValue(a.Id, out var id) ? id : null)).ToList();
    }

    public async Task<AgvDto> GetAgvAsync(Guid id, CancellationToken ct = default)
    {
        var a = await agvs.GetAsync(id, ct) ?? throw AppException.NotFound("AGV_NOT_FOUND", "AGV not found.");
        var active = await tasks.GetActiveForAgvAsync(id, ct);
        return a.ToDto(active?.Id);
    }

    public async Task<IReadOnlyList<LocationDto>> ListLocationsAsync(CancellationToken ct = default)
    {
        var occ = await racks.CountByLocationAsync(ct);
        var res = await reservations.CountActiveDestinationByLocationAsync(ct);
        return (await locations.ListAsync(ct))
            .Select(l => l.ToDto(occ.GetValueOrDefault(l.Id), res.GetValueOrDefault(l.Id))).ToList();
    }

    /// <summary>CR-057: id and code of the removed locations, so that past records can still show them.</summary>
    public async Task<IReadOnlyList<RemovedLocationDto>> ListRemovedLocationsAsync(CancellationToken ct = default) =>
        (await locations.ListRemovedAsync(ct)).Select(l => new RemovedLocationDto(l.Id, l.Code)).ToList();

    /// <summary>CR-058: codes that belonged to a removed location and to no present one; the deployed flow list leaves them out.</summary>
    public async Task<IReadOnlySet<string>> RemovedOnlyCodesAsync(CancellationToken ct = default)
    {
        var codes = (await locations.ListRemovedAsync(ct)).Select(l => l.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (codes.Count > 0) codes.ExceptWith((await locations.ListAsync(ct)).Select(l => l.Code));
        return codes;
    }

    public async Task<LocationDto> GetLocationAsync(string code, CancellationToken ct = default)
    {
        var l = await locations.GetByCodeAsync(code, ct) ?? throw AppException.NotFound("LOCATION_NOT_FOUND", $"Location '{code}' not found.");
        return l.ToDto(await racks.CountAtLocationAsync(l.Id, ct), await reservations.CountActiveDestinationAsync(l.Id, ct));
    }

    public async Task<IReadOnlyList<RackDto>> ListRacksAsync(string? locationCode, RackStatus? status, int take, CancellationToken ct = default)
    {
        Guid? locId = null;
        if (!string.IsNullOrWhiteSpace(locationCode))
            locId = (await locations.GetByCodeAsync(locationCode, ct) ?? throw AppException.NotFound("LOCATION_NOT_FOUND", $"Location '{locationCode}' not found.")).Id;
        return (await racks.ListAsync(locId, status, Math.Clamp(take, 1, 1000), ct)).Select(r => r.ToDto()).ToList();
    }

    public async Task<IReadOnlyList<InventoryTransactionDto>> ListTransactionsAsync(string? locationCode, Guid? taskId, int take, CancellationToken ct = default)
    {
        Guid? locId = null;
        if (!string.IsNullOrWhiteSpace(locationCode))
            locId = (await locations.GetByCodeAsync(locationCode, ct) ?? throw AppException.NotFound("LOCATION_NOT_FOUND", $"Location '{locationCode}' not found.")).Id;
        return (await transactions.ListAsync(locId, taskId, Math.Clamp(take, 1, 1000), ct)).Select(t => t.ToDto()).ToList();
    }

    public async Task<PagedResult<TaskDto>> ListTasksAsync(TaskState? state, Guid? agvId, int skip, int take, CancellationToken ct = default)
    {
        skip = Math.Max(0, skip); take = Math.Clamp(take, 1, 200);
        var (items, total) = await tasks.QueryAsync(new TaskQuery(state, agvId, skip, take), ct);
        return new PagedResult<TaskDto>(items.Select(t => t.ToDto()).ToList(), total, skip, take);
    }

    public async Task<TaskDto> GetTaskAsync(Guid id, CancellationToken ct = default) =>
        (await tasks.GetAsync(id, ct) ?? throw AppException.NotFound("TASK_NOT_FOUND", "Task not found.")).ToDto();

    public async Task<IReadOnlyList<TaskHistoryDto>> GetTaskHistoryAsync(Guid id, CancellationToken ct = default)
    {
        var t = await tasks.GetAsync(id, ct) ?? throw AppException.NotFound("TASK_NOT_FOUND", "Task not found.");
        return t.History.OrderBy(h => h.OccurredAt).Select(h => h.ToDto()).ToList();
    }
}

/// <summary>CR-057: a location that was removed from the system.</summary>
public sealed record RemovedLocationDto(Guid Id, string Code);
