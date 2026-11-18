using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WCS.Application;
using WCS.Application.Abstractions;
using WCS.Application.Auth;
using WCS.Application.Common;
using WCS.Application.Config;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Tasks;
using WCS.Domain.Users;
using WCS.Infrastructure;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Simulation;

namespace WCS.Api.Api;

/// <summary>Runs an operation and writes one audit entry for the outcome (success, denied or failed). Secrets must not be in the snapshots.</summary>
internal static class Audited
{
    public static async Task<T> RunAsync<T>(HttpContext c, string action, string? targetType, string? targetId, object? before,
        Func<Task<T>> operation, Func<T, object?>? after = null, string? detail = null, bool auditSuccess = true)
    {
        var log = c.RequestServices.GetRequiredService<IAuditLog>();
        var actor = c.Actor();
        try
        {
            var result = await operation();
            if (auditSuccess) await log.WriteAsync(new AuditRecord(action, actor.Username, actor.Role.ToString(), targetType, targetId, AuditResult.Success, detail,
                before, after?.Invoke(result), actor.IpAddress), c.RequestAborted);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var code = ex switch { AppException a => a.Code, DomainException d => d.Code, _ => "ERROR" };
            var denied = ex is AppException { Kind: ErrorKind.Forbidden or ErrorKind.Unauthorized };
            await log.WriteAsync(new AuditRecord(action, actor.Username, actor.Role.ToString(), targetType, targetId,
                denied ? AuditResult.Denied : AuditResult.Failed, $"{code}: {ex.Message}", before, null, actor.IpAddress), CancellationToken.None);
            throw;
        }
    }

    public static Task WriteAsync(HttpContext c, string action, string? targetType, string? targetId, string? detail = null) =>
        c.RequestServices.GetRequiredService<IAuditLog>().WriteAsync(new AuditRecord(action, c.Actor().Username, c.Actor().Role.ToString(),
            targetType, targetId, AuditResult.Success, detail, IpAddress: c.Actor().IpAddress), c.RequestAborted);

    public static object TaskSnapshot(TaskDto t) => new { t.Id, t.Flow, t.State, t.RequestLocationId, t.SourceLocationId, t.DestinationLocationId, t.AgvId };
}

public static class Endpoints
{
    private static readonly DateTimeOffset BackendStart = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public static void MapWcsApi(this WebApplication app)
    {
        // ---------------------------------------------------------------- authentication (no role required, login is anonymous)
        var auth = app.MapGroup("/api/auth").WithTags("Auth").AddEndpointFilter<ValidationFilter>();
        auth.MapPost("login", async (HttpContext c, LoginRequest r, AuthService svc, CancellationToken ct) =>
        {
            var user = await svc.LoginAsync(r.Username, r.Password, c.Connection.RemoteIpAddress?.ToString(), ct);
            await c.SignInAsync(user);
            return ApiResults.Ok(c, Me(user.Username, user.DisplayName, user.Role, user.MustChangePassword));
        }).AllowAnonymous();
        auth.MapPost("logout", async (HttpContext c, AuthService svc, CancellationToken ct) =>
        {
            await svc.LogoutAsync(c.Actor(), ct);
            await c.SignOutAsync(AuthSetup.Scheme);
            return ApiResults.Ok(c, new { signedOut = true });
        }).RequireAuthorization();
        auth.MapGet("me", (HttpContext c) =>
            ApiResults.Ok(c, Me(c.User.Identity!.Name!, c.User.DisplayName(), c.Actor().Role, c.User.MustChangePassword()))).RequireAuthorization();
        // Personal interface preferences: any signed-in user, always the caller's own account (taken from the session).
        auth.MapGet("preferences", async (HttpContext c, AuthService svc, CancellationToken ct) =>
            ApiResults.Ok(c, await svc.GetPreferencesAsync(c.Actor(), ct))).RequireAuthorization();
        auth.MapPut("preferences", async (HttpContext c, UserPreferencesRequest r, AuthService svc, CancellationToken ct) =>
            ApiResults.Ok(c, await svc.SetPreferencesAsync(c.Actor(), r.Theme, r.SidebarCollapsed, ct))).RequireAuthorization();
        // Own profile: full name + e-mail (self-service), avatar. Always the caller's account (from the session); there is no route that addresses another user.
        auth.MapGet("profile", async (HttpContext c, ProfileService svc, CancellationToken ct) =>
            ApiResults.Ok(c, await svc.GetAsync(c.Actor(), ct))).RequireAuthorization();
        auth.MapPut("profile", async (HttpContext c, UpdateProfileRequest r, ProfileService svc, CancellationToken ct) =>
        {
            var (profile, identity) = await svc.UpdateOwnAsync(c.Actor(), r.DisplayName, r.Email, ct);
            await c.SignInAsync(identity); // the display name is a session claim: refresh it (the security stamp is unchanged, other sessions stay valid)
            return ApiResults.Ok(c, profile);
        }).RequireAuthorization();
        auth.MapGet("avatar", async (HttpContext c, ProfileService svc, CancellationToken ct) =>
        {
            var file = await svc.GetAvatarAsync(c.Actor(), ct);
            if (file is null) return ApiResults.Fail(c, StatusCodes.Status404NotFound, "AVATAR_NOT_FOUND", "Chưa có ảnh đại diện.");
            // The stored (validated) type is the only type served; the browser may not sniff it into anything else and may not run it as a document.
            c.Response.Headers["X-Content-Type-Options"] = "nosniff";
            c.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
            c.Response.Headers["Cache-Control"] = "private, no-store";
            return Results.File(file.Data, file.ContentType);
        }).RequireAuthorization();
        auth.MapPut("avatar", async (HttpContext c, ProfileService svc, CancellationToken ct) =>
        {
            // Raw image bytes in the body (no file name, no path, no multipart): read at most limit+1 bytes so an oversized upload is refused without buffering it.
            if (c.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } f) f.MaxRequestBodySize = AvatarImage.MaxBytes + 1024;
            using var ms = new MemoryStream();
            var buffer = new byte[16 * 1024]; int n;
            while ((n = await c.Request.Body.ReadAsync(buffer, ct)) > 0)
            {
                ms.Write(buffer, 0, n);
                if (ms.Length > AvatarImage.MaxBytes) throw AppException.Validation("AVATAR_TOO_LARGE", "Ảnh đại diện tối đa 512 KB.");
            }
            return ApiResults.Ok(c, await svc.SetAvatarAsync(c.Actor(), ms.ToArray(), ct));
        }).RequireAuthorization();
        auth.MapDelete("avatar", async (HttpContext c, ProfileService svc, CancellationToken ct) =>
            ApiResults.Ok(c, await svc.RemoveAvatarAsync(c.Actor(), ct))).RequireAuthorization();
        auth.MapPost("change-password", async (HttpContext c, ChangePasswordRequest r, AuthService svc, CancellationToken ct) =>
        {
            var user = await svc.ChangeOwnPasswordAsync(c.Actor(), r.CurrentPassword, r.NewPassword, ct);
            await c.SignInAsync(user); // the security stamp changed: re-issue this session, every other session of the account ends
            return ApiResults.Ok(c, Me(user.Username, user.DisplayName, user.Role, user.MustChangePassword));
        }).RequireAuthorization();

        // ---------------------------------------------------------------- operational API (Admin + Operator)
        var api = app.MapGroup("/api").AddEndpointFilter<ValidationFilter>().RequireAuthorization(AuthSetup.OperationsPolicy);
        var admin = AuthSetup.AdminPolicy;

        // ---- AGV
        var agv = api.MapGroup("/agvs").WithTags("AGV");
        agv.MapGet("", async (HttpContext c, WcsQueries q, CancellationToken ct) => ApiResults.Ok(c, await q.ListAgvsAsync(ct)));
        agv.MapGet("{id:guid}", async (HttpContext c, Guid id, WcsQueries q, CancellationToken ct) => ApiResults.Ok(c, await q.GetAgvAsync(id, ct)));
        // CR-008B-5 (D-8): Admin only, both confirmations mandatory, audited with the AGV, the task and both confirmations.
        agv.MapPost("{id:guid}/release-hold", async (HttpContext c, Guid id, ReleaseAgvHoldRequest r, AgvHoldService s, WcsQueries q, CancellationToken ct) =>
        {
            var before = await q.GetAgvAsync(id, ct);
            await Audited.RunAsync(c, "AGV_DISPATCH_HOLD_RELEASE", "Agv", id.ToString(), before,
                () => s.ReleaseAsync(id, r.InventoryReconciled, r.AgvPhysicallyVerified, r.Note, c.Actor().Username, ct),
                res => new { agv = res.Agv.Code, taskId = res.TaskId, previousReason = res.PreviousReason, heldAt = res.HeldAt, inventoryReconciled = r.InventoryReconciled, agvPhysicallyVerified = r.AgvPhysicallyVerified },
                r.Note);
            return ApiResults.Ok(c, await q.GetAgvAsync(id, ct));
        }).RequireAuthorization(admin);

        // ---- Location
        var loc = api.MapGroup("/locations").WithTags("Location");
        loc.MapGet("", async (HttpContext c, WcsQueries q, CancellationToken ct) => ApiResults.Ok(c, await q.ListLocationsAsync(ct)));
        loc.MapGet("{code}", async (HttpContext c, string code, WcsQueries q, CancellationToken ct) => ApiResults.Ok(c, await q.GetLocationAsync(code, ct)));

        // ---- Inventory (owned by WCS). CR-001 (approved 2026-11-06): both roles may read AND use the existing stock operations; every change is audited.
        var inv = api.MapGroup("/inventory").WithTags("Inventory");
        inv.MapGet("racks", async (HttpContext c, WcsQueries q, string? location, RackStatus? status, int? take, CancellationToken ct) =>
            ApiResults.Ok(c, await q.ListRacksAsync(location, status, take ?? 200, ct)));
        inv.MapGet("transactions", async (HttpContext c, WcsQueries q, string? location, Guid? taskId, int? take, CancellationToken ct) =>
            ApiResults.Ok(c, await q.ListTransactionsAsync(location, taskId, take ?? 100, ct)));
        inv.MapPost("initial-stock", async (HttpContext c, InitialStockRequest r, IInventoryService svc, CancellationToken ct) =>
        {
            var ids = await Audited.RunAsync(c, "INVENTORY_INITIAL_STOCK", "Location", r.LocationCode, null,
                () => svc.DeclareInitialStockAsync(r.LocationCode, r.RackKind, r.Quantity, c.Actor().Username, ct),
                res => new { r.LocationCode, rackKind = r.RackKind.ToString(), created = res.Count });
            return ApiResults.Ok(c, new { created = ids.Count, rackIds = ids }, StatusCodes.Status201Created);
        });
        inv.MapPost("adjustments", async (HttpContext c, StockAdjustmentRequest r, IInventoryService svc, CancellationToken ct) =>
        {
            var qty = await Audited.RunAsync(c, "INVENTORY_ADJUST", "Location", r.LocationCode, null,
                () => svc.AdjustStockAsync(r.LocationCode, r.RackKind, r.TargetQuantity, c.Actor().Username, r.Note, ct),
                res => new { r.LocationCode, rackKind = r.RackKind.ToString(), target = r.TargetQuantity, quantity = res }, r.Note);
            return ApiResults.Ok(c, new { quantity = qty });
        });

        // ---- PLC topology, IO catalog, monitoring (CR-002)
        api.MapPlcApi();
        api.MapRoutingApi();   // CR-010

        // ---- Process flows available for task requests (read-only view of the deployed configuration; lets the UI avoid hard-coding stations)
        api.MapGet("/flows", (HttpContext c, WCS.Application.Flows.IFlowCatalog flows) =>
            ApiResults.Ok(c, flows.All.OrderBy(f => f.Flow).Select(f => new
            {
                flow = f.Flow.ToString(),
                rackKind = f.RackKind.ToString(),
                stations = f.AllowedRequestStations.OrderBy(x => x).ToArray(),
            }))).WithTags("Flow");

        // ---- Task
        var task = api.MapGroup("/tasks").WithTags("Task");
        task.MapGet("", async (HttpContext c, WcsQueries q, TaskState? state, Guid? agvId, int? skip, int? take, CancellationToken ct) =>
            ApiResults.Ok(c, await q.ListTasksAsync(state, agvId, skip ?? 0, take ?? 50, ct)));
        task.MapGet("{id:guid}", async (HttpContext c, Guid id, WcsQueries q, CancellationToken ct) => ApiResults.Ok(c, await q.GetTaskAsync(id, ct)));
        task.MapGet("{id:guid}/history", async (HttpContext c, Guid id, WcsQueries q, CancellationToken ct) =>
            ApiResults.Ok(c, await q.GetTaskHistoryAsync(id, ct)));
        task.MapPost("", async (HttpContext c, CreateTaskRequest r, ITaskOrchestrator o, CancellationToken ct) =>
        {
            // RequestedBy always comes from the session; a value sent in the body is ignored.
            var result = await Audited.RunAsync(c, "TASK_CREATE", "Task", null, null,
                () => o.CreateTaskAsync(new CreateTaskCommand(r.RequestKey, r.Flow, r.StationCode, c.Actor().Username), ct),
                res => Audited.TaskSnapshot(res.Task), $"flow={r.Flow}, station={r.StationCode}");
            return ApiResults.Ok(c, result.Task, result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        });
        task.MapPut("{id:guid}", async (HttpContext c, Guid id, UpdateTaskRequest r, ITaskOrchestrator o, WcsQueries q, CancellationToken ct) =>
        {
            var before = await q.GetTaskAsync(id, ct);
            var updated = await Audited.RunAsync(c, "TASK_UPDATE", "Task", id.ToString(), Audited.TaskSnapshot(before),
                () => o.UpdateTaskAsync(id, r.Flow, r.StationCode, r.ExpectedState, c.Actor(), ct), Audited.TaskSnapshot);
            return ApiResults.Ok(c, updated);
        });
        task.MapDelete("{id:guid}", async (HttpContext c, Guid id, string? expectedState, ITaskOrchestrator o, WcsQueries q, CancellationToken ct) =>
        {
            var before = await q.GetTaskAsync(id, ct);
            // The success entry is written INSIDE the delete transaction by the orchestrator; only failures are audited here.
            var deleted = await Audited.RunAsync(c, "TASK_DELETE", "Task", id.ToString(), Audited.TaskSnapshot(before),
                () => o.DeleteTaskAsync(id, expectedState, c.Actor(), ct), auditSuccess: false);
            return ApiResults.Ok(c, deleted);
        });
        // Cancelling or reconciling a task that an AGV already took is an exception-handling action: Admin only.
        task.MapPost("{id:guid}/cancel", async (HttpContext c, Guid id, CancelTaskRequest r, ITaskOrchestrator o, WcsQueries q, CancellationToken ct) =>
        {
            var before = await q.GetTaskAsync(id, ct);
            return ApiResults.Ok(c, await Audited.RunAsync(c, "TASK_CANCEL", "Task", id.ToString(), Audited.TaskSnapshot(before),
                () => o.CancelTaskAsync(id, r.Reason, c.Actor().Username, ct), Audited.TaskSnapshot, r.Reason));
        }).RequireAuthorization(admin);
        // CR-008B-3: reconciliation worklist and detail for the Admin (read only), and the "could not be established" check (records history, releases nothing).
        task.MapGet("reconciliation", async (HttpContext c, ReconciliationService s, int? take, CancellationToken ct) =>
            ApiResults.Ok(c, await s.ListAsync(take ?? 200, ct))).RequireAuthorization(admin);
        task.MapGet("{id:guid}/reconciliation", async (HttpContext c, Guid id, ReconciliationService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.GetDetailAsync(id, ct))).RequireAuthorization(admin);
        task.MapPost("{id:guid}/reconciliation-check", async (HttpContext c, Guid id, ReconciliationCheckRequest r, ReconciliationService s, WcsQueries q, CancellationToken ct) =>
        {
            await Audited.RunAsync(c, "TASK_RECONCILE_CHECK_UNRESOLVED", "Task", id.ToString(), null,
                async () => { await s.RecordUnresolvedCheckAsync(id, r.Note.Trim(), c.Actor().Username, ct); return 0; }, _ => new { unresolved = true }, r.Note);
            return ApiResults.Ok(c, await q.GetTaskAsync(id, ct));
        }).RequireAuthorization(admin);
        task.MapPost("{id:guid}/reconcile", async (HttpContext c, Guid id, ReconcileTaskRequest r, IInventoryService inv2, WcsQueries q, CancellationToken ct) =>
        {
            await Audited.RunAsync(c, "TASK_RECONCILE", "Task", id.ToString(), null,
                async () => { await inv2.ReconcileTaskAsync(id, r.ActualLocationCode, c.Actor().Username, r.Note, ct); return 0; },
                _ => new { r.ActualLocationCode }, r.Note);
            return ApiResults.Ok(c, await q.GetTaskAsync(id, ct));
        }).RequireAuthorization(admin);

        // ---- Configuration (connections + operational parameters): Admin only. Secrets are never returned.
        var cfg = api.MapGroup("/config").WithTags("Configuration").RequireAuthorization(admin);
        cfg.MapGet("plc", async (HttpContext c, ConnectionConfigService s, CancellationToken ct) => ApiResults.Ok(c, await s.GetPlcAsync(ct)));
        cfg.MapPut("plc", async (HttpContext c, PlcSettingsRequest r, ConnectionConfigService s, CancellationToken ct) =>
        {
            var before = await s.GetPlcAsync(ct);
            return ApiResults.Ok(c, await Audited.RunAsync(c, "CONFIG_PLC_UPDATE", "ConnectionSetting", "Plc", before,
                () => s.UpdatePlcAsync(new UpdatePlcCommand(r.Enabled,
                    new PlcSettings(r.DeviceName, r.IpAddress, r.Port, r.Protocol, r.NetworkNumber, r.PcNumber, r.StationNumber, r.TimeoutMs, r.PollingIntervalMs),
                    c.Actor().Username), ct), res => res));
        });
        cfg.MapPost("plc/test", async (HttpContext c, ConnectionConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "CONFIG_PLC_TEST", "ConnectionSetting", "Plc", null, () => s.TestAsync(ConnectionKind.Plc, ct), res => res)));
        cfg.MapGet("rcs", async (HttpContext c, ConnectionConfigService s, CancellationToken ct) => ApiResults.Ok(c, await s.GetRcsAsync(ct)));
        cfg.MapPut("rcs", async (HttpContext c, RcsSettingsRequest r, ConnectionConfigService s, CancellationToken ct) =>
        {
            var before = await s.GetRcsAsync(ct);
            // Only the FACT that a secret was replaced/cleared is recorded, never its value.
            var secretNote = string.Join(", ", new[]
            {
                r.Password is not null ? "password replaced" : null, r.ClearPassword ? "password cleared" : null,
                r.Token is not null ? "token replaced" : null, r.ClearToken ? "token cleared" : null,
            }.Where(x => x is not null));
            return ApiResults.Ok(c, await Audited.RunAsync(c, "CONFIG_RCS_UPDATE", "ConnectionSetting", "Rcs", before,
                () => s.UpdateRcsAsync(new UpdateRcsCommand(r.Enabled,
                    new RcsSettings(r.SystemName, r.Host, r.Port, r.BaseUrl, r.AuthMode ?? ConnectionOptions.AuthNone, r.Username, r.TimeoutMs),
                    r.Password, r.ClearPassword, r.Token, r.ClearToken, c.Actor().Username), ct), res => res, secretNote.Length == 0 ? null : secretNote));
        });
        cfg.MapPost("rcs/test", async (HttpContext c, ConnectionConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "CONFIG_RCS_TEST", "ConnectionSetting", "Rcs", null, () => s.TestAsync(ConnectionKind.Rcs, ct), res => res)));
        cfg.MapGet("operations", async (HttpContext c, OperationalConfigService s, CancellationToken ct) => ApiResults.Ok(c, await s.GetAsync(ct)));
        cfg.MapPut("agvs/{id:guid}", async (HttpContext c, Guid id, AgvConfigRequest r, OperationalConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "CONFIG_AGV_UPDATE", "Agv", id.ToString(), null,
                () => s.UpdateAgvAsync(id, new UpdateAgvCommand(r.RcsRobotCode, r.Enabled), ct), res => res)));
        cfg.MapPost("locations", async (HttpContext c, LocationConfigRequest r, OperationalConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "CONFIG_LOCATION_CREATE", "Location", r.Code, null,
                () => s.SaveLocationAsync(new SaveLocationCommand(r.Code, r.Name, r.Type, r.RackCapacity, r.AllowedRackKind, r.Enabled, r.RequiresPlcPermission), true, ct),
                res => res), StatusCodes.Status201Created));
        cfg.MapPut("locations/{code}", async (HttpContext c, string code, LocationConfigRequest r, OperationalConfigService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "CONFIG_LOCATION_UPDATE", "Location", code, null,
                () => s.SaveLocationAsync(new SaveLocationCommand(code, r.Name, r.Type, r.RackCapacity, r.AllowedRackKind, r.Enabled, r.RequiresPlcPermission), false, ct),
                res => res)));

        // ---- Account management and audit trail: Admin only
        var users = api.MapGroup("/users").WithTags("Users").RequireAuthorization(admin);
        users.MapGet("", async (HttpContext c, string? search, UserAdminService s, CancellationToken ct) => ApiResults.Ok(c, await s.ListAsync(c.Actor(), search, ct)));
        users.MapPost("", async (HttpContext c, CreateUserRequest r, UserAdminService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.CreateAsync(c.Actor(), new CreateUserCommand(r.Username, r.DisplayName, r.Role, r.Password, r.IsActive ?? true), ct), StatusCodes.Status201Created));
        users.MapPut("{id:guid}", async (HttpContext c, Guid id, UpdateUserRequest r, UserAdminService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.UpdateAsync(c.Actor(), id, new UpdateUserCommand(r.DisplayName, r.Role, r.IsActive), ct)));
        users.MapPut("{id:guid}/organization", async (HttpContext c, Guid id, UpdateOrganizationRequest r, UserAdminService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.SetOrganizationAsync(c.Actor(), id, r.Position, r.Department, ct)));
        users.MapDelete("{id:guid}", async (HttpContext c, Guid id, UserAdminService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(c.Actor(), id, ct);
            return ApiResults.Ok(c, new { deleted = true });
        });
        users.MapPost("{id:guid}/reset-password", async (HttpContext c, Guid id, ResetPasswordRequest r, UserAdminService s, CancellationToken ct) =>
        {
            await s.ResetPasswordAsync(c.Actor(), id, r.NewPassword, ct);
            return ApiResults.Ok(c, new { reset = true });
        });
        api.MapGet("/audit", async (HttpContext c, IAuditLog log, string? actor, string? action, AuditResult? result, DateTimeOffset? from, DateTimeOffset? to,
            int? skip, int? take, string? actionPrefix, CancellationToken ct) =>
            ApiResults.Ok(c, await log.QueryAsync(new AuditQuery(actor, action, result, from, to, skip ?? 0, take ?? 100, actionPrefix), ct)))
            .WithTags("Audit").RequireAuthorization(admin);

        // ---- System
        // Server resources (container and host metrics, kept apart): Admin only, served from the one background sampler (it never samples per request).
        api.MapGet("/system/resources", (HttpContext c, WCS.Application.Monitoring.IResourceMonitor monitor) => ApiResults.Ok(c, monitor.GetSnapshot()))
            .WithTags("System").RequireAuthorization(admin);
        api.MapGet("/system/health", async (HttpContext c, WcsDbContext db, ISchedulerStatus scheduler, IPlcAdapter plc, WCS.Application.Plc.IPlcRuntime plcRuntime,
            IOptions<IntegrationOptions> integration, IOptions<SchedulerOptions> schedulerOptions, IRcsDispatchGuard rcsGuard, IClock clock, CancellationToken ct) =>
        {
            var dbWatch = System.Diagnostics.Stopwatch.StartNew();
            var dbOk = await db.Database.CanConnectAsync(ct);
            dbWatch.Stop();
            var last = scheduler.LastRunAt;
            var stale = last is null || clock.UtcNow - last > TimeSpan.FromMilliseconds(Math.Max(10_000, schedulerOptions.Value.TickIntervalMs * 10));
            var health = new
            {
                status = dbOk && !stale && scheduler.LastError is null ? "Healthy" : "Degraded",
                time = clock.UtcNow,
                // Application health only: the backend process and its database. It says nothing about PLC, RCS or AGV (see integration) nor about the server resources (see /system/resources).
                backend = new { startedAt = BackendStart, uptimeSeconds = Math.Max(0, Math.Round((clock.UtcNow - BackendStart).TotalSeconds)) },
                database = new { connected = dbOk, latencyMs = dbOk ? Math.Round(dbWatch.Elapsed.TotalMilliseconds, 1) : (double?)null },
                scheduler = new { lastRunAt = last, lastError = scheduler.LastError, stale },
                integration = new
                {
                    mode = integration.Value.Mode, plc = plc.GetConnectionStatus().ToString(),
                    rcs = new { dispatchAllowed = rcsGuard.Check().Allowed, code = rcsGuard.Check().Code },   // CR-008A: RCS_NOT_CONNECTED in Production without a real adapter
                    plcRuntime = new
                    {
                        mode = plcRuntime.Info().EffectiveMode, running = plcRuntime.Running,
                        connected = plcRuntime.Workers(false).Count(w => w.State == "Connected"), total = plcRuntime.Workers(false).Count,
                    },
                },
            };
            return ApiResults.Ok(c, health, dbOk ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        }).WithTags("System");
    }

    private static object Me(string username, string displayName, UserRole role, bool mustChangePassword) =>
        new { username, displayName, role = role.ToString(), mustChangePassword };

    /// <summary>
    /// Simulator-only endpoints (Admin only). They inject vendor-neutral signals, NOT the RCS callback API (no real RCS endpoint is assumed).
    /// Registered only when Integration:Mode = Simulator, and never in Production unless explicitly enabled.
    /// </summary>
    public static void MapSimulatorApi(this WebApplication app)
    {
        var sim = app.MapGroup("/api/simulator").WithTags("Simulator").AddEndpointFilter<ValidationFilter>().RequireAuthorization(AuthSetup.AdminPolicy);
        sim.MapPost("tasks/{id:guid}/advance", async (HttpContext c, Guid id, SimulationDriver d, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "SIMULATOR_ADVANCE", "Task", id.ToString(), null, () => d.AdvanceAsync(id, ct), res => new { res.Signal, res.Message })));
        sim.MapPost("tasks/{id:guid}/run", async (HttpContext c, Guid id, SimulationDriver d, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "SIMULATOR_RUN", "Task", id.ToString(), null, () => d.RunAsync(id, 20, ct), res => new { steps = res.Count })));
        sim.MapPost("station-requests", async (HttpContext c, SimulatedStationRequest r, StationSignalSimulator s) =>
        {
            await Audited.RunAsync(c, "SIMULATOR_STATION_REQUEST", "Location", r.StationCode, null, () =>
            {
                s.EnqueueRequest(new StationRequest(r.StationCode, r.Flow, r.RequestKey));
                return Task.FromResult(0);
            }, _ => new { r.StationCode, r.Flow, r.RequestKey });
            return ApiResults.Ok(c, new { queued = true }, StatusCodes.Status202Accepted);
        });
    }
}
