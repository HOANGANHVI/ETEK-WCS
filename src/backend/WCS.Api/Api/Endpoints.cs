using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WCS.Application;
using WCS.Application.Abstractions;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Tasks;
using WCS.Infrastructure;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Simulation;

namespace WCS.Api.Api;

public static class Endpoints
{
    public static void MapWcsApi(this WebApplication app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<ValidationFilter>();

        // ---- AGV
        var agv = api.MapGroup("/agvs").WithTags("AGV");
        agv.MapGet("", async (HttpContext c, WcsQueries q, CancellationToken ct) => ApiResults.Ok(c, await q.ListAgvsAsync(ct)));
        agv.MapGet("{id:guid}", async (HttpContext c, Guid id, WcsQueries q, CancellationToken ct) => ApiResults.Ok(c, await q.GetAgvAsync(id, ct)));

        // ---- Location
        var loc = api.MapGroup("/locations").WithTags("Location");
        loc.MapGet("", async (HttpContext c, WcsQueries q, CancellationToken ct) => ApiResults.Ok(c, await q.ListLocationsAsync(ct)));
        loc.MapGet("{code}", async (HttpContext c, string code, WcsQueries q, CancellationToken ct) => ApiResults.Ok(c, await q.GetLocationAsync(code, ct)));

        // ---- Inventory (owned by WCS)
        var inv = api.MapGroup("/inventory").WithTags("Inventory");
        inv.MapGet("racks", async (HttpContext c, WcsQueries q, string? location, RackStatus? status, int? take, CancellationToken ct) =>
            ApiResults.Ok(c, await q.ListRacksAsync(location, status, take ?? 200, ct)));
        inv.MapGet("transactions", async (HttpContext c, WcsQueries q, string? location, Guid? taskId, int? take, CancellationToken ct) =>
            ApiResults.Ok(c, await q.ListTransactionsAsync(location, taskId, take ?? 100, ct)));
        inv.MapPost("initial-stock", async (HttpContext c, InitialStockRequest r, IInventoryService svc, CancellationToken ct) =>
        {
            var ids = await svc.DeclareInitialStockAsync(r.LocationCode, r.RackKind, r.Quantity, r.User, ct);
            return ApiResults.Ok(c, new { created = ids.Count, rackIds = ids }, StatusCodes.Status201Created);
        });
        inv.MapPost("adjustments", async (HttpContext c, StockAdjustmentRequest r, IInventoryService svc, CancellationToken ct) =>
        {
            var qty = await svc.AdjustStockAsync(r.LocationCode, r.RackKind, r.TargetQuantity, r.User, r.Note, ct);
            return ApiResults.Ok(c, new { quantity = qty });
        });

        // ---- Task
        var task = api.MapGroup("/tasks").WithTags("Task");
        task.MapGet("", async (HttpContext c, WcsQueries q, TaskState? state, Guid? agvId, int? skip, int? take, CancellationToken ct) =>
            ApiResults.Ok(c, await q.ListTasksAsync(state, agvId, skip ?? 0, take ?? 50, ct)));
        task.MapGet("{id:guid}", async (HttpContext c, Guid id, WcsQueries q, CancellationToken ct) => ApiResults.Ok(c, await q.GetTaskAsync(id, ct)));
        task.MapGet("{id:guid}/history", async (HttpContext c, Guid id, WcsQueries q, CancellationToken ct) =>
            ApiResults.Ok(c, await q.GetTaskHistoryAsync(id, ct)));
        task.MapPost("", async (HttpContext c, CreateTaskRequest r, ITaskOrchestrator o, CancellationToken ct) =>
        {
            var result = await o.CreateTaskAsync(new CreateTaskCommand(r.RequestKey, r.Flow, r.StationCode, r.RequestedBy), ct);
            return ApiResults.Ok(c, result.Task, result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        });
        task.MapPost("{id:guid}/cancel", async (HttpContext c, Guid id, CancelTaskRequest r, ITaskOrchestrator o, CancellationToken ct) =>
            ApiResults.Ok(c, await o.CancelTaskAsync(id, r.Reason, r.User, ct)));
        task.MapPost("{id:guid}/reconcile", async (HttpContext c, Guid id, ReconcileTaskRequest r, IInventoryService inv2, WcsQueries q, CancellationToken ct) =>
        {
            await inv2.ReconcileTaskAsync(id, r.ActualLocationCode, r.User, r.Note, ct);
            return ApiResults.Ok(c, await q.GetTaskAsync(id, ct));
        });

        // ---- System
        api.MapGet("/system/health", async (HttpContext c, WcsDbContext db, ISchedulerStatus scheduler, IPlcAdapter plc,
            IOptions<IntegrationOptions> integration, IOptions<SchedulerOptions> schedulerOptions, IClock clock, CancellationToken ct) =>
        {
            var dbOk = await db.Database.CanConnectAsync(ct);
            var last = scheduler.LastRunAt;
            var stale = last is null || clock.UtcNow - last > TimeSpan.FromMilliseconds(Math.Max(10_000, schedulerOptions.Value.TickIntervalMs * 10));
            var health = new
            {
                status = dbOk && !stale && scheduler.LastError is null ? "Healthy" : "Degraded",
                time = clock.UtcNow,
                database = new { connected = dbOk },
                scheduler = new { lastRunAt = last, lastError = scheduler.LastError, stale },
                integration = new { mode = integration.Value.Mode, plc = plc.GetConnectionStatus().ToString() },
            };
            return ApiResults.Ok(c, health, dbOk ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        }).WithTags("System");
    }

    /// <summary>
    /// Simulator-only endpoints. They inject vendor-neutral signals, NOT the RCS callback API (no real RCS endpoint is assumed).
    /// Registered only when Integration:Mode = Simulator, and never in Production unless explicitly enabled.
    /// </summary>
    public static void MapSimulatorApi(this WebApplication app)
    {
        var sim = app.MapGroup("/api/simulator").WithTags("Simulator").AddEndpointFilter<ValidationFilter>();
        sim.MapPost("tasks/{id:guid}/advance", async (HttpContext c, Guid id, SimulationDriver d, CancellationToken ct) =>
            ApiResults.Ok(c, await d.AdvanceAsync(id, ct)));
        sim.MapPost("tasks/{id:guid}/run", async (HttpContext c, Guid id, SimulationDriver d, CancellationToken ct) =>
            ApiResults.Ok(c, await d.RunAsync(id, 20, ct)));
        sim.MapPost("station-requests", (HttpContext c, SimulatedStationRequest r, StationSignalSimulator s) =>
        {
            s.EnqueueRequest(new StationRequest(r.StationCode, r.Flow, r.RequestKey));
            return ApiResults.Ok(c, new { queued = true }, StatusCodes.Status202Accepted);
        });
    }
}
