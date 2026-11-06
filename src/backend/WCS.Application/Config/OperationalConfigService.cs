using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
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

public sealed record UpdateAgvCommand(string? RcsRobotCode, bool Enabled);
public sealed record SaveLocationCommand(string Code, string Name, LocationType Type, int RackCapacity, AllowedRackKind AllowedRackKind,
    bool Enabled, bool RequiresPlcPermission);

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
    IUnitOfWork uow,
    IAgvRepository agvs,
    ILocationRepository locations,
    IRackRepository racks,
    IReservationRepository reservations,
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
        if (code is not null && !RobotCodeRegex().IsMatch(code)) errors.Add(new("rcsRobotCode", "Mã robot chỉ gồm chữ, số, dấu chấm, gạch ngang, gạch dưới (tối đa 100 ký tự)."));
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        await uow.ExecuteAsync(async t =>
        {
            var agv = await agvs.GetAsync(id, t) ?? throw AppException.NotFound("AGV_NOT_FOUND", "Không tìm thấy AGV.");
            if (code is not null && (await agvs.ListAsync(t)).Any(a => a.Id != id && string.Equals(a.RcsRobotCode, code, StringComparison.OrdinalIgnoreCase)))
                throw new ValidationFailedException([new("rcsRobotCode", "Mã robot này đã được gán cho AGV khác.")]);
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

    public async Task<LocationDto> SaveLocationAsync(SaveLocationCommand cmd, bool create, CancellationToken ct = default)
    {
        var code = ConnectionValidator.Clean(cmd.Code) ?? "";
        var name = ConnectionValidator.Clean(cmd.Name);
        var errors = new List<FieldError>();
        if (!LocationCodeRegex().IsMatch(code)) errors.Add(new("code", "Mã vị trí chỉ gồm chữ, số, gạch ngang, gạch dưới (tối đa 50 ký tự)."));
        if (name is null) errors.Add(new("name", "Cần nhập tên vị trí."));
        else if (name.Length > 200) errors.Add(new("name", "Tên vị trí tối đa 200 ký tự."));
        if (cmd.RackCapacity is < 0 or > 1000) errors.Add(new("rackCapacity", "Sức chứa phải nằm trong khoảng 0–1000."));
        if (!Enum.IsDefined(cmd.Type)) errors.Add(new("type", "Loại vị trí không hợp lệ."));
        if (!Enum.IsDefined(cmd.AllowedRackKind)) errors.Add(new("allowedRackKind", "Loại rack chấp nhận không hợp lệ."));
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        await uow.ExecuteAsync(async t =>
        {
            var existing = await locations.GetByCodeAsync(code, t);
            if (create)
            {
                if (existing is not null) throw new ValidationFailedException([new("code", "Mã vị trí đã tồn tại.")]);
                locations.Add(new Location(code, name!, cmd.Type, cmd.RackCapacity, cmd.AllowedRackKind, cmd.RequiresPlcPermission, clock.UtcNow));
                return true;
            }
            if (existing is null) throw AppException.NotFound("LOCATION_NOT_FOUND", "Không tìm thấy vị trí.");

            var occupied = await racks.CountAtLocationAsync(existing.Id, t);
            var reserved = await reservations.CountActiveDestinationAsync(existing.Id, t);
            if (cmd.RackCapacity < occupied + reserved)
                throw new ValidationFailedException([new("rackCapacity", $"Sức chứa không được nhỏ hơn số rack đang có ({occupied}) cộng số chỗ đang giữ ({reserved}).")]);
            if (!cmd.Enabled && existing.Enabled && reserved > 0)
                throw AppException.Conflict("LOCATION_HAS_RESERVATION", "Vị trí đang được giữ chỗ cho nhiệm vụ nên chưa thể tắt.");
            existing.Configure(name!, cmd.RackCapacity, cmd.AllowedRackKind, cmd.Enabled, cmd.RequiresPlcPermission, clock.UtcNow);
            return true;
        }, ct);
        logger.LogInformation("Location {Code} {Action}", code, create ? "created" : "updated");
        await notifier.PublishAsync([new WcsEvent("InventoryChanged", new { location = code })], ct);
        return await queries.GetLocationAsync(code, ct);
    }
}
