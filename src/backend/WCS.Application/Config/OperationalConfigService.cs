using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Routing;
using WCS.Application.Flows;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Locations;

namespace WCS.Application.Config;

public sealed record PolicyView(string Concurrency, bool ConcurrencyApproved, string QueueOrder, bool QueueOrderApproved, string NoSourceBehavior, bool NoSourceBehaviorApproved);
public sealed record SchedulerView(int TickIntervalMs, int MaxDispatchAttempts, IReadOnlyDictionary<string, int> StateTimeoutsSeconds);
public sealed record FlowView(string Flow, string RackKind, string AgvRole, IReadOnlyList<string> Stations, string SourceMode,
    IReadOnlyList<IReadOnlyList<string>> SourceGroups, string DestinationMode, IReadOnlyList<IReadOnlyList<string>> DestinationGroups,
    IReadOnlyList<string> NoBindDropLocations);
public sealed record OperationsView(PolicyView Policy, SchedulerView Scheduler, IReadOnlyList<FlowView> Flows, IReadOnlyList<AgvDto> Agvs,
    IReadOnlyList<LocationDto> Locations);

/// <summary>CR-057: one reason a location is kept; <see cref="Detail"/> = a count or the ports / processes concerned.</summary>
public sealed record LocationBlocker(string Code, string Detail);
public sealed record LocationRemovalDto(string Code, bool CanDelete, IReadOnlyList<LocationBlocker> Blockers, IReadOnlyList<LocationBlocker> Warnings);

public sealed record UpdateAgvCommand(string? RcsRobotCode, bool Enabled);
public sealed record SaveLocationCommand(string Code, string Name, LocationType Type, int RackCapacity, AllowedRackKind AllowedRackKind,
    bool Enabled, bool RequiresPlcPermission, Guid? ProductModelId = null, StationRole? StationRole = null);

/// <summary>
/// Operational parameters. Policy, scheduler and flow rules come from deployment configuration (appsettings) and are shown read-only –
/// there is no approved way to change them at runtime and no new rule is invented here. AGV robot codes and locations are configuration
/// DATA stored in the database and can be edited.
/// </summary>
public sealed partial class OperationalConfigService(
    WcsQueries queries,
    IFlowCatalog flows,
    IOptions<SchedulerOptions> scheduler,
    IOptions<ProcessFlowOptions> flowOptions,
    IOptions<RouteEngineOptions> routeOptions,
    IUnitOfWork uow,
    IAgvRepository agvs,
    ILocationRepository locations,
    IRackRepository racks,
    IReservationRepository reservations,
    IProductModelRepository productModels,
    IRoutingRepository routingRepo,
    ITransportTaskRepository tasks,
    IWcsNotifier notifier,
    IClock clock,
    ILogger<OperationalConfigService> logger)
{
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}$")] private static partial Regex RobotCodeRegex();
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,50}$")] private static partial Regex LocationCodeRegex();

    public async Task<OperationsView> GetAsync(CancellationToken ct = default)
    {
        var s = scheduler.Value;
        return new OperationsView(
            // Concurrency is an approved rule (BR-01). Queue order and no-source behaviour are NOT approved: shown as such.
            new PolicyView("PER_AGV", true, "FIFO", false, flowOptions.Value.NoSourceBehavior, false),
            new SchedulerView(s.TickIntervalMs, s.MaxDispatchAttempts, new Dictionary<string, int>(s.StateTimeoutsSeconds)),
            flows.All.OrderBy(f => f.Flow).Select(f => new FlowView(f.Flow.ToString(), f.RackKind.ToString(), f.AgvRole.ToString(),
                f.AllowedRequestStations.Order().ToList(), f.SourceMode.ToString(), f.SourceGroups, f.DestinationMode.ToString(), f.DestinationGroups,
                f.NoBindDropLocations.Order().ToList())).ToList(),
            await queries.ListAgvsAsync(ct), await queries.ListLocationsAsync(ct));
    }

    public async Task<AgvDto> UpdateAgvAsync(Guid id, UpdateAgvCommand cmd, CancellationToken ct = default)
    {
        var code = ConnectionValidator.Clean(cmd.RcsRobotCode);
        var errors = new List<FieldError>();
        if (code is not null && !RobotCodeRegex().IsMatch(code)) errors.Add(new("rcsRobotCode", "Mã robot chỉ gồm chữ, số, dấu chấm, gạch ngang, gạch dưới (tối đa 100 ký tự).", "ROBOT_CODE_FORMAT"));
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        await uow.ExecuteAsync(async t =>
        {
            var agv = await agvs.GetAsync(id, t) ?? throw AppException.NotFound("AGV_NOT_FOUND", "Không tìm thấy AGV.");
            if (code is not null && (await agvs.ListAsync(t)).Any(a => a.Id != id && string.Equals(a.RcsRobotCode, code, StringComparison.OrdinalIgnoreCase)))
                throw new ValidationFailedException([new("rcsRobotCode", "Mã robot này đã được gán cho AGV khác.", "ROBOT_CODE_IN_USE")]);
            // PER_AGV safety: an AGV that currently owns an active task must not be switched off underneath it.
            if (!cmd.Enabled && agv.Enabled && await tasks.GetActiveForAgvAsync(id, t) is not null)
                throw AppException.Conflict("AGV_HAS_ACTIVE_TASK", "AGV đang có nhiệm vụ hoạt động nên chưa thể tắt.");
            agv.SetRobotCode(code);
            agv.SetEnabled(cmd.Enabled);
            return true;
        }, ct);
        logger.LogInformation("AGV configuration updated (enabled={Enabled}, robotCodeSet={Set})", cmd.Enabled, code is not null);
        await notifier.PublishAsync([new WcsEvent("AgvStatusChanged", new { id })], ct);
        return await queries.GetAgvAsync(id, ct);
    }

    private async Task EnsureModelExistsAsync(Guid? modelId, CancellationToken ct)
    {
        if (modelId is null) return;
        var m = await productModels.GetAsync(modelId.Value, ct) ?? throw AppException.NotFound("MODEL_NOT_FOUND", "Product model not found.");
        if (!m.Enabled) throw AppException.Rejected("MODEL_DISABLED", $"Product model {m.Code} is disabled.");
    }

    // ------------------------------------------------------------------ CR-057: removal

    /// <summary>
    /// What stops a location from being removed. It is removed only when nothing depends on it any more: not protected, no rack on it, no PLC port
    /// linked to it, in no process definition and in no unfinished work (a use by the deployment configuration is a warning, CR-058). Finished records stay: the row is only marked, so history keeps showing the code, and the code can be used again.
    /// </summary>
    public Task<LocationRemovalDto> RemovalAsync(string code, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var location = await locations.GetByCodeAsync(code, t) ?? throw AppException.NotFound("LOCATION_NOT_FOUND", "Không tìm thấy vị trí.");
        return await PlanRemovalAsync(location, t);
    }, ct);

    public async Task<string> DeleteLocationAsync(string code, string? reason, string? confirmCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw AppException.Validation("REASON_REQUIRED", "A reason is required.");
        if (reason.Length > 500) throw AppException.Validation("REASON_TOO_LONG", "The reason has at most 500 characters.");
        var removed = await uow.ExecuteAsync(async t =>
        {
            var location = await locations.GetByCodeAsync(code, t) ?? throw AppException.NotFound("LOCATION_NOT_FOUND", "Không tìm thấy vị trí.");
            if (!string.Equals(confirmCode?.Trim(), location.Code, StringComparison.OrdinalIgnoreCase))
                throw AppException.Validation("LOCATION_CODE_CONFIRMATION", "Type the location code to confirm the removal.");
            var plan = await PlanRemovalAsync(location, t);
            if (plan.Blockers.Count > 0) throw AppException.Rejected(plan.Blockers[0].Code, $"{location.Code} cannot be deleted: " + string.Join("; ", plan.Blockers.Select(b => $"{b.Code} {b.Detail}".Trim())));
            location.MarkRemoved(clock.UtcNow);   // the row version makes this conflict with an allocation made meanwhile
            return location.Code;
        }, ct);
        logger.LogInformation("Location {Code} deleted", removed);
        await notifier.PublishAsync([new WcsEvent("InventoryChanged", new { location = removed })], ct);
        return removed;
    }

    private async Task<LocationRemovalDto> PlanRemovalAsync(Location location, CancellationToken ct)
    {
        var code = location.Code;
        bool Names(IEnumerable<string> list) => list.Contains(code, StringComparer.OrdinalIgnoreCase);
        var blockers = new List<LocationBlocker>();
        if (Names(routeOptions.Value.ProtectedLocationCodes)) blockers.Add(new("LOCATION_DELETE_PROTECTED", ""));
        var usage = await locations.UsageAsync(location, ct);
        if (usage.Racks > 0) blockers.Add(new("LOCATION_DELETE_RACKS", usage.Racks.ToString()));
        if (usage.Ports.Count > 0) blockers.Add(new("LOCATION_DELETE_PORT", string.Join(", ", usage.Ports)));
        if (usage.Processes.Count > 0) blockers.Add(new("LOCATION_DELETE_PROCESS", string.Join(", ", usage.Processes)));
        if (usage.OpenWork > 0) blockers.Add(new("LOCATION_DELETE_OPEN_WORK", usage.OpenWork.ToString()));
        return new LocationRemovalDto(code, blockers.Count == 0, blockers, ConfiguredUse(code));
    }

    /// <summary>
    /// CR-058: the deployment configuration (single-task flows, PLC location codes) no longer keeps a location. Each use is a warning the Admin sees before confirming: afterwards a flow
    /// skips the removed source or destination, and a request from the removed station or PLC code is refused.
    /// </summary>
    private List<LocationBlocker> ConfiguredUse(string code)
    {
        bool Names(IEnumerable<string> list) => list.Contains(code, StringComparer.OrdinalIgnoreCase);
        var warnings = new List<LocationBlocker>();
        void Flows(string warning, Func<FlowRuleOptions, bool> uses)
        {
            var names = flowOptions.Value.Flows.Where(f => uses(f.Value)).Select(f => f.Key.ToUpperInvariant().Replace("BP", "BP-")).Order(StringComparer.Ordinal).ToList();
            if (names.Count > 0) warnings.Add(new(warning, string.Join(", ", names)));
        }
        Flows("LOCATION_WARN_FLOW_STATION", f => Names(f.AllowedRequestStations));
        Flows("LOCATION_WARN_FLOW_SOURCE", f => f.SourceGroups.Any(Names));
        Flows("LOCATION_WARN_FLOW_DESTINATION", f => f.DestinationGroups.Any(Names));
        var plc = routeOptions.Value.PlcLocationCodes.Where(x => string.Equals(x.Value, code, StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).Order(StringComparer.Ordinal).ToList();
        if (plc.Count > 0) warnings.Add(new("LOCATION_WARN_PLC_CODE", string.Join(", ", plc)));
        return warnings;
    }

    public async Task<LocationDto> SaveLocationAsync(SaveLocationCommand cmd, bool create, CancellationToken ct = default)
    {
        var code = ConnectionValidator.Clean(cmd.Code) ?? "";
        var name = ConnectionValidator.Clean(cmd.Name);
        var errors = new List<FieldError>();
        if (!LocationCodeRegex().IsMatch(code)) errors.Add(new("code", "Mã vị trí chỉ gồm chữ, số, gạch ngang, gạch dưới (tối đa 50 ký tự).", "LOCATION_CODE_FORMAT"));
        if (name is null) errors.Add(new("name", "Cần nhập tên vị trí.", "LOCATION_NAME_REQUIRED"));
        else if (name.Length > 200) errors.Add(new("name", "Tên vị trí tối đa 200 ký tự.", "LOCATION_NAME_MAX"));
        if (cmd.RackCapacity is < 0 or > 1000) errors.Add(new("rackCapacity", "Sức chứa phải nằm trong khoảng 0–1000.", "CAPACITY_RANGE"));
        if (!Enum.IsDefined(cmd.Type)) errors.Add(new("type", "Loại vị trí không hợp lệ.", "LOCATION_TYPE_INVALID"));
        if (!Enum.IsDefined(cmd.AllowedRackKind)) errors.Add(new("allowedRackKind", "Loại rack chấp nhận không hợp lệ.", "ALLOWED_KIND_INVALID"));
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        await uow.ExecuteAsync(async t =>
        {
            var existing = await locations.GetByCodeAsync(code, t);
            if (create)
            {
                if (existing is not null) throw new ValidationFailedException([new("code", "Mã vị trí đã tồn tại.", "LOCATION_EXISTS")]);
                var created = new Location(code, name!, cmd.Type, cmd.RackCapacity, cmd.AllowedRackKind, cmd.RequiresPlcPermission, clock.UtcNow);
                if (cmd.ProductModelId is not null || cmd.StationRole is not null)
                {
                    await EnsureModelExistsAsync(cmd.ProductModelId, t);
                    created.SetModelAndRole(cmd.ProductModelId, cmd.StationRole, clock.UtcNow);
                }
                locations.Add(created);
                return true;
            }
            if (existing is null) throw AppException.NotFound("LOCATION_NOT_FOUND", "Không tìm thấy vị trí.");

            var occupied = await racks.CountAtLocationAsync(existing.Id, t);
            var reserved = await reservations.CountActiveDestinationAsync(existing.Id, t);
            if (cmd.RackCapacity < occupied + reserved)
                throw new ValidationFailedException([new("rackCapacity", $"Sức chứa không được nhỏ hơn số rack đang có ({occupied}) cộng số chỗ đang giữ ({reserved}).", "CAPACITY_BELOW_USED", FieldArgs.Of(("occupied", occupied), ("reserved", reserved)))]);
            if (!cmd.Enabled && existing.Enabled && reserved > 0)
                throw AppException.Conflict("LOCATION_HAS_RESERVATION", "Vị trí đang được giữ chỗ cho nhiệm vụ nên chưa thể tắt.");
            existing.Configure(name!, cmd.RackCapacity, cmd.AllowedRackKind, cmd.Enabled, cmd.RequiresPlcPermission, clock.UtcNow);
            // CR-036: NULL in the command = "not given": the stored model / role stay as they are (an old package or client never clears them). A change needs an empty location.
            var newModel = cmd.ProductModelId ?? existing.ProductModelId;
            var newRole = cmd.StationRole ?? existing.StationRole;
            if (newModel != existing.ProductModelId || newRole != existing.StationRole)
            {
                await EnsureModelExistsAsync(cmd.ProductModelId, t);
                if (occupied > 0) throw AppException.Conflict("LOCATION_HAS_RACKS", "The product model / station role of a location that holds racks cannot be changed.");
                if (await routingRepo.IsLocationReferencedByPublishedAsync(existing.Code, t)) throw AppException.Conflict("LOCATION_REFERENCED_BY_PUBLISHED", "The product model / station role of a location used by a Published process version cannot be changed.");
                existing.SetModelAndRole(newModel, newRole, clock.UtcNow);
            }
            return true;
        }, ct);
        logger.LogInformation("Location {Code} {Action}", code, create ? "created" : "updated");
        await notifier.PublishAsync([new WcsEvent("InventoryChanged", new { location = code })], ct);
        return await queries.GetLocationAsync(code, ct);
    }
}
