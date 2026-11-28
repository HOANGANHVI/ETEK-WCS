using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Routing;
using WCS.Application.Services;
using WCS.Domain.Common;

namespace WCS.Application.Flows;

/// <summary>A Phase 1 flow as the request forms see it. <see cref="Available"/> = the process module lets a new task be created for it now.</summary>
public sealed record TaskFlowView(string Flow, string RackKind, string[] Stations, bool Available);

/// <summary>
/// CR-063: the process module ("process and route settings") is the only source of what may be created. A Phase 1 flow is offered while the module holds the process of the same code
/// (BP-01 for Bp01), published, switched on and able to accept requests; its stations are the configured request stations that the process also names as triggers and that exist.
/// This governs requests made by a person (the task API). Requests the PLC raises and tasks that already exist are not touched.
/// </summary>
public sealed class TaskFlowAvailability(IFlowCatalog flows, ProcessAdminService processes, WcsQueries queries, IOptions<ProcessFlowOptions> options,
    ITransportTaskRepository tasks, IUnitOfWork uow)
{
    public static string ProcessCodeOf(ProcessFlow flow) => flow.ToString().ToUpperInvariant().Replace("BP", "BP-");

    public async Task<IReadOnlyList<TaskFlowView>> ListAsync(CancellationToken ct = default)
    {
        var require = options.Value.RequireModuleProcess;
        var removed = await queries.RemovedOnlyCodesAsync(ct);
        var published = require ? await processes.StartableAsync(ct) : [];
        return flows.All.OrderBy(f => f.Flow).Select(f =>
        {
            var stations = f.AllowedRequestStations.Where(x => !removed.Contains(x)).OrderBy(x => x).ToArray();
            if (!require) return new TaskFlowView(f.Flow.ToString(), f.RackKind.ToString(), stations, true);
            var process = published.FirstOrDefault(p => p.AcceptsRequests && string.Equals(p.Code, ProcessCodeOf(f.Flow), StringComparison.OrdinalIgnoreCase));
            var offered = process is null ? [] : stations.Where(x => process.TriggerLocationCodes.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray();
            return new TaskFlowView(f.Flow.ToString(), f.RackKind.ToString(), offered.Length > 0 ? offered : stations, offered.Length > 0);
        }).ToList();
    }

    /// <summary>Refuses a new task for a flow the module does not offer. A repeated RequestKey passes (it returns the task it already made); an unknown flow is left to the orchestrator.</summary>
    public async Task EnsureAvailableAsync(string? requestKey, string flowText, string stationCode, CancellationToken ct = default)
    {
        if (!options.Value.RequireModuleProcess || !flows.TryParseFlow(flowText, out var flow)) return;
        if (!string.IsNullOrWhiteSpace(requestKey) && await uow.ExecuteAsync(t => tasks.GetByRequestKeyAsync(requestKey.Trim(), t), ct) is not null) return;
        var view = (await ListAsync(ct)).FirstOrDefault(v => v.Flow == flow.ToString());
        if (view is null || !view.Available)
            throw AppException.Rejected("FLOW_NOT_AVAILABLE", $"Process {ProcessCodeOf(flow)} is not published and ready in the process settings: no task can be created for it.");
        if (!view.Stations.Contains((stationCode ?? "").Trim(), StringComparer.OrdinalIgnoreCase))
            throw AppException.Rejected("STATION_NOT_ALLOWED", $"Station {stationCode} is not a request station of {ProcessCodeOf(flow)}.");
    }
}
