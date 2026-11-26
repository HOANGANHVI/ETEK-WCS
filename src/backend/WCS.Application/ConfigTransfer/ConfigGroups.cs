using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WCS.Application.Abstractions;
using WCS.Application.Auth;
using WCS.Application.Common;
using WCS.Application.Config;
using WCS.Application.Plc;
using WCS.Application.Routing;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Domain.Users;

namespace WCS.Application.ConfigTransfer;

/// <summary>What the planner knows while it checks one record: the current state of this system and the (selected) records of the package, per group.</summary>
public sealed class PlanContext(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ConfigRecord>> target, IReadOnlyDictionary<string, IReadOnlyDictionary<string, ConfigRecord>> package)
{
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ConfigRecord>> Target { get; } = target;
    /// <summary>Package records that take part in the import (not excluded by the user).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ConfigRecord>> Package { get; } = package;

    public bool LocationExists(string code) =>
        Target.GetValueOrDefault(ConfigGroupIds.Location)?.ContainsKey(code) == true || Package.GetValueOrDefault(ConfigGroupIds.Location)?.ContainsKey(code) == true;
}

public sealed record ApplyContext(Guid ImportId, ActorInfo Actor)
{
    public string Detail => $"CR-026 import {ImportId:D}";
}

/// <summary>One configuration group. A group reads its records, checks them against this system and writes ONLY through the existing business services.</summary>
public interface IConfigGroup
{
    string Id { get; }
    string File { get; }
    IReadOnlyList<string> DependsOn { get; }
    /// <summary>CR-036 (A.1): groups that must be APPLIED first when they are part of the same import, without being a dependency (exports of other groups are not forced to include them).</summary>
    IReadOnlyList<string> ApplyAfter => [];
    /// <summary>Only records that already exist here may be updated; nothing is created (AGV, PLC Station in Phase 2).</summary>
    bool UpdateOnly { get; }
    IReadOnlyList<string> Validate(ConfigRecord record);
    Task<IReadOnlyList<ConfigRecord>> ReadAsync(CancellationToken ct);
    IReadOnlyList<FieldChange> Diff(ConfigRecord package, ConfigRecord target);
    Task<(string Code, string Message)?> CheckAsync(ConfigRecord package, ConfigRecord? target, PlanContext ctx, CancellationToken ct);
    Task ApplyAsync(IReadOnlyList<(PlanItem Item, ConfigRecord Package, ConfigRecord? Target)> items, ApplyContext ctx, CancellationToken ct);
}

internal static class GroupKit
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public static IReadOnlyList<FieldChange> DiffFields(ConfigRecord package, ConfigRecord target, params string[] names)
    {
        var list = new List<FieldChange>();
        foreach (var n in names)
        {
            var a = ConfigPackage.Render(package.Fields[n]); var b = ConfigPackage.Render(target.Fields[n]);
            if (a != b) list.Add(new FieldChange(n, b, a));
        }
        return list;
    }

    public static string? Str(ConfigRecord r, string f) => r.Fields[f] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    public static int? Int(ConfigRecord r, string f) => r.Fields[f] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
    public static bool? Bool(ConfigRecord r, string f) => r.Fields[f] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    public static void Audit(IAuditLog audit, ApplyContext ctx, string action, string targetType, string targetId, object? before, object? after) =>
        audit.Enlist(new AuditRecord(action, ctx.Actor.Username, ctx.Actor.Role.ToString(), targetType, targetId, AuditResult.Success, ctx.Detail, before, after, ctx.Actor.IpAddress));

    public static string? RequireString(ConfigRecord r, string f, int max, List<string> problems, bool required = true)
    {
        var s = Str(r, f);
        if (string.IsNullOrWhiteSpace(s)) { if (required) problems.Add($"'{f}' is required."); return null; }
        if (s.Length > max) problems.Add($"'{f}' is longer than {max} characters.");
        return s;
    }
}

// ====================================================================================================================== Location
public sealed partial class LocationConfigGroup(IUnitOfWork uow, ILocationRepository locations, IRackRepository racks, IReservationRepository reservations,
    OperationalConfigService ops, IAuditLog audit, IProductModelRepository models, IRoutingRepository routing) : IConfigGroup
{
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,50}$")] private static partial Regex CodeRegex();
    private static readonly string[] Compared = ["name", "type", "rackCapacity", "allowedRackKind", "enabled", "requiresPlcPermission"];

    public string Id => ConfigGroupIds.Location;
    public string File => "locations.json";
    public IReadOnlyList<string> DependsOn => [];
    public IReadOnlyList<string> ApplyAfter => [ConfigGroupIds.ProductModel];   // CR-036 (A.1)
    public bool UpdateOnly => false;
    // CR-036 (A.1): optional fields. They are written only when the location has a value, and an import only acts on a field that is PRESENT in the package: an old package never clears them.
    private static readonly string[] Optional = ["productModelCode", "stationRole"];

    public IReadOnlyList<string> Validate(ConfigRecord r)
    {
        var p = new List<string>();
        if (!CodeRegex().IsMatch(r.Key)) p.Add("the code may contain letters, digits, '-' and '_' (max 50).");
        GroupKit.RequireString(r, "name", 200, p);
        if (!Enum.TryParse<LocationType>(GroupKit.Str(r, "type"), out var t) || !Enum.IsDefined(t)) p.Add("'type' is not a valid location type.");
        if (GroupKit.Int(r, "rackCapacity") is not (>= 0 and <= 1000)) p.Add("'rackCapacity' must be 0-1000.");
        if (!Enum.TryParse<AllowedRackKind>(GroupKit.Str(r, "allowedRackKind"), out var k) || !Enum.IsDefined(k)) p.Add("'allowedRackKind' is not valid.");
        if (GroupKit.Bool(r, "enabled") is null) p.Add("'enabled' is required.");
        if (GroupKit.Bool(r, "requiresPlcPermission") is null) p.Add("'requiresPlcPermission' is required.");
        if (r.Fields.ContainsKey("productModelCode") && (GroupKit.Str(r, "productModelCode") is not { Length: > 0 and <= 40 })) p.Add("'productModelCode' must be a model code (max 40 characters) when present.");
        if (r.Fields.ContainsKey("stationRole") && (!Enum.TryParse<StationRole>(GroupKit.Str(r, "stationRole"), out var sr) || !Enum.IsDefined(sr))) p.Add("'stationRole' is not a valid station role.");
        return p;
    }

    public Task<IReadOnlyList<ConfigRecord>> ReadAsync(CancellationToken ct) => uow.ExecuteAsync<IReadOnlyList<ConfigRecord>>(async t =>
    {
        var codeOf = (await models.ListAsync(t)).ToDictionary(m => m.Id, m => m.Code);
        return (await locations.ListAsync(t)).OrderBy(l => l.Code, StringComparer.Ordinal).Select(l =>
        {
            var o = new JsonObject
            {
                ["name"] = l.Name, ["type"] = l.Type.ToString(), ["rackCapacity"] = l.RackCapacity, ["allowedRackKind"] = l.AllowedRackKind.ToString(),
                ["enabled"] = l.Enabled, ["requiresPlcPermission"] = l.RequiresPlcPermission,
            };
            if (l.ProductModelId is { } mid && codeOf.TryGetValue(mid, out var code)) o["productModelCode"] = code;   // by code, never by Guid
            if (l.StationRole is { } role) o["stationRole"] = role.ToString();
            return new ConfigRecord(l.Code, o);
        }).ToList();
    }, ct);

    public IReadOnlyList<FieldChange> Diff(ConfigRecord package, ConfigRecord target)
    {
        var list = GroupKit.DiffFields(package, target, Compared).ToList();
        foreach (var f in Optional)
        {
            if (!package.Fields.ContainsKey(f)) continue;   // an old package: nothing to compare, nothing to change
            var a = ConfigPackage.Render(package.Fields[f]); var b = ConfigPackage.Render(target.Fields.TryGetPropertyValue(f, out var tv) ? tv : null);
            if (a != b) list.Add(new FieldChange(f, b, a));
        }
        return list;
    }

    public async Task<(string Code, string Message)?> CheckAsync(ConfigRecord package, ConfigRecord? target, PlanContext ctx, CancellationToken ct)
    {
        // CR-036 (A.1): the model / role of a package must refer to a model that exists here or comes with the package, and may not change a location that holds racks or is used by a Published version
        var pkgModel = package.Fields.ContainsKey("productModelCode") ? GroupKit.Str(package, "productModelCode") : null;
        if (pkgModel is not null && ctx.Target.GetValueOrDefault(ConfigGroupIds.ProductModel)?.ContainsKey(pkgModel) != true && ctx.Package.GetValueOrDefault(ConfigGroupIds.ProductModel)?.ContainsKey(pkgModel) != true)
            return ("MISSING_DEPENDENCY", $"Location {package.Key} belongs to product model {pkgModel}, which does not exist here and is not part of the selected import.");
        if (target is null) return null;
        var modelChanged = package.Fields.ContainsKey("productModelCode") && GroupKit.Str(package, "productModelCode") != (target.Fields.ContainsKey("productModelCode") ? GroupKit.Str(target, "productModelCode") : null);
        var roleChanged = package.Fields.ContainsKey("stationRole") && GroupKit.Str(package, "stationRole") != (target.Fields.ContainsKey("stationRole") ? GroupKit.Str(target, "stationRole") : null);
        if (modelChanged || roleChanged)
        {
            var guard = await uow.ExecuteAsync<(string, string)?>(async t =>
            {
                var loc = await locations.GetByCodeAsync(package.Key, t);
                if (loc is null) return null;
                if (await racks.CountAtLocationAsync(loc.Id, t) > 0) return ("LOCATION_HAS_RACKS", $"Location {package.Key} holds racks: its product model / station role cannot be changed by an import.");
                if (await routing.IsLocationReferencedByPublishedAsync(package.Key, t)) return ("LOCATION_REFERENCED_BY_PUBLISHED", $"Location {package.Key} is used by a Published process version: its product model / station role cannot be changed by an import.");
                return null;
            }, ct);
            if (guard is not null) return guard;
        }
        // the existing update path does not change the type of a location: report it instead of pretending
        if (GroupKit.Str(package, "type") != GroupKit.Str(target, "type"))
            return ("LOCATION_TYPE_CHANGE_NOT_SUPPORTED", $"Location {package.Key}: the type cannot be changed by an import ({GroupKit.Str(target, "type")} here, {GroupKit.Str(package, "type")} in the package).");
        return await uow.ExecuteAsync<(string, string)?>(async t =>
        {
            var loc = await locations.GetByCodeAsync(package.Key, t);
            if (loc is null) return null;
            var used = await racks.CountAtLocationAsync(loc.Id, t); var reserved = await reservations.CountActiveDestinationAsync(loc.Id, t);
            var cap = GroupKit.Int(package, "rackCapacity") ?? 0;
            if (cap < used + reserved) return ("CAPACITY_BELOW_USED", $"Location {package.Key}: capacity {cap} is below the racks it holds ({used}) plus reserved places ({reserved}).");
            if (GroupKit.Bool(package, "enabled") == false && loc.Enabled && reserved > 0) return ("LOCATION_HAS_RESERVATION", $"Location {package.Key} has reserved places and cannot be disabled.");
            return null;
        }, ct);
    }

    public async Task ApplyAsync(IReadOnlyList<(PlanItem Item, ConfigRecord Package, ConfigRecord? Target)> items, ApplyContext ctx, CancellationToken ct)
    {
        foreach (var (item, pkg, target) in items)
        {
            var create = item.Action == PlanAction.Create;
            var type = Enum.Parse<LocationType>(create ? GroupKit.Str(pkg, "type")! : GroupKit.Str(target!, "type")!);   // an existing location keeps its type
            // CR-036 (A.1): only a field that is present in the package acts; an absent field is "not given" (unchanged), never "clear"
            Guid? modelId = null; StationRole? role = null;
            if (pkg.Fields.ContainsKey("productModelCode") && GroupKit.Str(pkg, "productModelCode") is { } mc)
                modelId = (await uow.ExecuteAsync(async t => await models.GetByCodeAsync(mc, t), ct))?.Id ?? throw AppException.NotFound("MODEL_NOT_FOUND", $"Product model {mc} not found.");
            if (pkg.Fields.ContainsKey("stationRole") && Enum.TryParse<StationRole>(GroupKit.Str(pkg, "stationRole"), out var sr)) role = sr;
            await ops.SaveLocationAsync(new SaveLocationCommand(pkg.Key, GroupKit.Str(pkg, "name")!, type, GroupKit.Int(pkg, "rackCapacity")!.Value,
                Enum.Parse<AllowedRackKind>(GroupKit.Str(pkg, "allowedRackKind")!), GroupKit.Bool(pkg, "enabled")!.Value, GroupKit.Bool(pkg, "requiresPlcPermission")!.Value, modelId, role), create, ct);
            GroupKit.Audit(audit, ctx, create ? "CONFIG_LOCATION_CREATE" : "CONFIG_LOCATION_UPDATE", "Location", pkg.Key, target?.Fields, pkg.Fields);
        }
    }
}

// ====================================================================================================================== AGV (update only)
public sealed partial class AgvConfigGroup(IUnitOfWork uow, IAgvRepository agvs, OperationalConfigService ops, IAuditLog audit) : IConfigGroup
{
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}$")] private static partial Regex RobotRegex();
    private static readonly string[] Compared = ["rcsRobotCode", "enabled"];

    public string Id => ConfigGroupIds.Agv;
    public string File => "agvs.json";
    public IReadOnlyList<string> DependsOn => [];
    public bool UpdateOnly => true;

    public IReadOnlyList<string> Validate(ConfigRecord r)
    {
        var p = new List<string>();
        if (r.Key.Length is 0 or > 50) p.Add("the AGV code is required (max 50).");
        if (!Enum.TryParse<AgvRole>(GroupKit.Str(r, "role"), out var role) || !Enum.IsDefined(role)) p.Add("'role' is not valid.");
        var robot = GroupKit.Str(r, "rcsRobotCode");
        if (robot is not null && !RobotRegex().IsMatch(robot)) p.Add("'rcsRobotCode' may contain letters, digits, '.', '-' and '_' (max 100).");
        if (GroupKit.Bool(r, "enabled") is null) p.Add("'enabled' is required.");
        return p;
    }

    public Task<IReadOnlyList<ConfigRecord>> ReadAsync(CancellationToken ct) => uow.ExecuteAsync<IReadOnlyList<ConfigRecord>>(async t =>
        (await agvs.ListAsync(t)).OrderBy(a => a.Code, StringComparer.Ordinal).Select(a => new ConfigRecord(a.Code, new JsonObject
        {
            ["role"] = a.Role.ToString(), ["rcsRobotCode"] = a.RcsRobotCode, ["enabled"] = a.Enabled,
        })).ToList(), ct);

    public IReadOnlyList<FieldChange> Diff(ConfigRecord package, ConfigRecord target) => GroupKit.DiffFields(package, target, Compared);

    public Task<(string Code, string Message)?> CheckAsync(ConfigRecord package, ConfigRecord? target, PlanContext ctx, CancellationToken ct)
    {
        (string, string)? Result()
        {
            if (target is null) return ("AGV_NOT_FOUND", $"AGV {package.Key} does not exist here; an import never creates an AGV.");
            if (GroupKit.Str(package, "role") != GroupKit.Str(target, "role"))
                return ("AGV_ROLE_MISMATCH", $"AGV {package.Key}: the role cannot be changed by an import ({GroupKit.Str(target, "role")} here, {GroupKit.Str(package, "role")} in the package).");
            var robot = GroupKit.Str(package, "rcsRobotCode");
            if (robot is not null)
            {
                var other = ctx.Target[Id].FirstOrDefault(o => o.Key != package.Key && string.Equals(GroupKit.Str(o.Value, "rcsRobotCode"), robot, StringComparison.OrdinalIgnoreCase));
                if (other.Key is not null) return ("ROBOT_CODE_IN_USE", $"AGV {package.Key}: robot code {robot} is already assigned to {other.Key}.");
            }
            return null;
        }
        return Task.FromResult(Result());
    }

    public async Task ApplyAsync(IReadOnlyList<(PlanItem Item, ConfigRecord Package, ConfigRecord? Target)> items, ApplyContext ctx, CancellationToken ct)
    {
        foreach (var (_, pkg, target) in items)
        {
            var id = await uow.ExecuteAsync(async t => (await agvs.GetByCodeAsync(pkg.Key, t))?.Id ?? throw AppException.NotFound("AGV_NOT_FOUND", $"AGV {pkg.Key} not found."), ct);
            await ops.UpdateAgvAsync(id, new UpdateAgvCommand(GroupKit.Str(pkg, "rcsRobotCode"), GroupKit.Bool(pkg, "enabled")!.Value), ct);
            GroupKit.Audit(audit, ctx, "CONFIG_AGV_UPDATE", "Agv", id.ToString(), target?.Fields, pkg.Fields);
        }
    }
}

// ====================================================================================================================== PLC Station (link + enabled, update only)
public sealed partial class PlcStationConfigGroup(IUnitOfWork uow, IPlcConfigRepository plc, PlcProvisioningService provisioning, IAuditLog audit) : IConfigGroup
{
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,50}$")] private static partial Regex CodeRegex();
    private static readonly string[] Compared = ["locationCode", "enabled"];

    public string Id => ConfigGroupIds.PlcStation;
    public string File => "plc-stations.json";
    public IReadOnlyList<string> DependsOn => [ConfigGroupIds.Location];
    public bool UpdateOnly => true;

    public IReadOnlyList<string> Validate(ConfigRecord r)
    {
        var p = new List<string>();
        if (r.Key.Length is 0 or > 50) p.Add("the station id is required (max 50).");
        GroupKit.RequireString(r, "plcId", 50, p);
        if (GroupKit.Int(r, "order") is null or < 0) p.Add("'order' is required.");
        var loc = GroupKit.Str(r, "locationCode");
        if (loc is not null && !CodeRegex().IsMatch(loc)) p.Add("'locationCode' is not a valid location code.");
        if (GroupKit.Bool(r, "enabled") is null) p.Add("'enabled' is required.");
        return p;
    }

    public Task<IReadOnlyList<ConfigRecord>> ReadAsync(CancellationToken ct) => uow.ExecuteAsync<IReadOnlyList<ConfigRecord>>(async t =>
        (await plc.ListStationsAsync(t)).OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => new ConfigRecord(s.Id, new JsonObject
        {
            ["plcId"] = s.PlcId, ["order"] = s.Order, ["locationCode"] = s.LocationCode, ["enabled"] = s.Enabled,
        })).ToList(), ct);

    public IReadOnlyList<FieldChange> Diff(ConfigRecord package, ConfigRecord target) => GroupKit.DiffFields(package, target, Compared);

    public Task<(string Code, string Message)?> CheckAsync(ConfigRecord package, ConfigRecord? target, PlanContext ctx, CancellationToken ct)
    {
        (string, string)? Result()
        {
            if (target is null) return ("STATION_NOT_FOUND", $"PLC station {package.Key} does not exist here; this import never creates a station, PLC connection or IO point.");
            if (GroupKit.Str(package, "plcId") != GroupKit.Str(target, "plcId") || GroupKit.Int(package, "order") != GroupKit.Int(target, "order"))
                return ("STATION_TOPOLOGY_MISMATCH", $"PLC station {package.Key}: the PLC or order differs ({GroupKit.Str(target, "plcId")}/{GroupKit.Int(target, "order")} here); the topology is not changed by an import.");
            var code = GroupKit.Str(package, "locationCode");
            if (code is null) return null;
            if (!ctx.LocationExists(code)) return ("MISSING_DEPENDENCY", $"PLC station {package.Key}: location {code} does not exist here and is not part of the selected import.");
            // another station of this system keeps the location, or two selected stations want it
            foreach (var (key, other) in ctx.Target[Id])
            {
                if (key == package.Key || GroupKit.Str(other, "locationCode") != code) continue;
                var staysThere = !ctx.Package[Id].TryGetValue(key, out var pkgOther) || GroupKit.Str(pkgOther, "locationCode") == code;
                if (staysThere) return ("LOCATION_ALREADY_LINKED", $"PLC station {package.Key}: location {code} is already linked to {key}.");
            }
            var rival = ctx.Package[Id].FirstOrDefault(o => o.Key != package.Key && GroupKit.Str(o.Value, "locationCode") == code);
            if (rival.Key is not null) return ("LOCATION_LINK_DUPLICATE", $"PLC station {package.Key}: the package links location {code} to {rival.Key} as well.");
            return null;
        }
        return Task.FromResult(Result());
    }

    public async Task ApplyAsync(IReadOnlyList<(PlanItem Item, ConfigRecord Package, ConfigRecord? Target)> items, ApplyContext ctx, CancellationToken ct)
    {
        // Phase A releases the location of every station whose link changes (so a swap between two stations cannot collide); phase B sets the final link.
        // Both phases run inside the one batch transaction: the transient state is never visible and a failure rolls everything back.
        foreach (var (_, pkg, target) in items)
            if (GroupKit.Str(target!, "locationCode") is not null && GroupKit.Str(target!, "locationCode") != GroupKit.Str(pkg, "locationCode"))
                await provisioning.SetPortLinkAsync(pkg.Key, null, GroupKit.Bool(target!, "enabled")!.Value, ct);
        foreach (var (_, pkg, target) in items)
        {
            await provisioning.SetPortLinkAsync(pkg.Key, GroupKit.Str(pkg, "locationCode"), GroupKit.Bool(pkg, "enabled")!.Value, ct);
            GroupKit.Audit(audit, ctx, "CONFIG_PLC_PORT_LINK", "PlcStation", pkg.Key, target?.Fields, pkg.Fields);
        }
    }
}

// ====================================================================================================================== Business Process (header + new Draft version)
public sealed partial class BusinessProcessConfigGroup(IUnitOfWork uow, IRoutingRepository routing, ProcessAdminService admin, IAuditLog audit, IProductModelRepository models) : IConfigGroup
{
    [GeneratedRegex(@"^[A-Z0-9._-]{1,60}$")] private static partial Regex CodeRegex();
    public static readonly string[] HeaderFields = ["name", "description", "triggerLocationCodes"];

    public string Id => ConfigGroupIds.BusinessProcess;
    public string File => "business-processes.json";
    public IReadOnlyList<string> DependsOn => [ConfigGroupIds.Location, ConfigGroupIds.PlcStation];
    public IReadOnlyList<string> ApplyAfter => [ConfigGroupIds.ProductModel];   // CR-036 (A.1)
    public bool UpdateOnly => false;

    private static VersionDefinition? Def(ConfigRecord r)
    {
        try { return r.Fields["definition"] is { } n ? JsonSerializer.Deserialize<VersionDefinition>(n.ToJsonString(), GroupKit.Json) : null; }
        catch (JsonException) { return null; }
    }
    private static string Canon(VersionDefinition d) => JsonSerializer.Serialize(d with { Note = null }, GroupKit.Json);

    public IReadOnlyList<string> Validate(ConfigRecord r)
    {
        var p = new List<string>();
        if (!CodeRegex().IsMatch(r.Key)) p.Add("the process code must be upper-case letters, digits, '.', '-' or '_' (max 60).");
        GroupKit.RequireString(r, "name", 200, p);
        try { DefinitionMapping.ParseRequestType(GroupKit.Str(r, "requestType") ?? ""); DefinitionMapping.ParseRackKind(GroupKit.Str(r, "rackKind") ?? ""); }
        catch (AppException e) { p.Add(e.Message); }
        if (r.Fields["triggerLocationCodes"] is not JsonArray) p.Add("'triggerLocationCodes' must be a list.");
        if (r.Fields.ContainsKey("productModelCode") && GroupKit.Str(r, "productModelCode") is not { Length: > 0 and <= 40 }) p.Add("'productModelCode' must be a model code (max 40 characters) when present.");
        var def = Def(r);
        if (def is null) { p.Add("'definition' is missing or malformed."); return p; }
        if (def.Steps.Count == 0) p.Add("the definition has no step.");
        else if (def.Steps.All(s => s.StepKey != def.FirstStepKey)) p.Add($"first step '{def.FirstStepKey}' is not a step of the definition.");
        try { foreach (var s in def.Steps) s.ToStep(); }
        catch (Exception e) when (e is AppException or DomainException) { p.Add(e.Message); }
        return p;
    }

    public Task<IReadOnlyList<ConfigRecord>> ReadAsync(CancellationToken ct) => uow.ExecuteAsync<IReadOnlyList<ConfigRecord>>(async t =>
    {
        var list = new List<ConfigRecord>();
        var codeOf = (await models.ListAsync(t)).ToDictionary(m => m.Id, m => m.Code);
        foreach (var p in (await routing.ListProcessesAsync(t)).OrderBy(x => x.Code, StringComparer.Ordinal))
        {
            var versions = await routing.ListVersionsAsync(p.Id, t);
            // the effective version: the Published one, else the latest
            var chosen = versions.FirstOrDefault(v => v.State == ProcessConfigState.Published) ?? versions.OrderByDescending(v => v.Number).FirstOrDefault();
            var full = chosen is null ? null : await routing.GetVersionAsync(chosen.Id, t);
            var def = full?.ToDefinition();
            var rec = new JsonObject
            {
                ["name"] = p.Name, ["description"] = p.Description, ["requestType"] = p.RequestType.ToString(), ["rackKind"] = p.RackKind.ToString(),
                ["triggerLocationCodes"] = new JsonArray(p.TriggerLocationCodes.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
                ["enabled"] = p.Enabled, ["reviewBlocked"] = p.ReviewBlocked, ["reviewReason"] = p.ReviewReason,
                ["definitionSource"] = chosen is null ? null : new JsonObject { ["number"] = chosen.Number, ["state"] = chosen.State.ToString() },
                ["definition"] = def is null ? null : JsonSerializer.SerializeToNode(def with { Note = null }, GroupKit.Json),
            };
            if (p.ProductModelId is { } pmid && codeOf.TryGetValue(pmid, out var pcode)) rec["productModelCode"] = pcode;   // by code, only when set
            if (p.RequiresReadiness) rec["requiresReadiness"] = true;   // CR-039: only when set
            if (p.QueueScope is { } queueScope) rec["queueScope"] = queueScope;   // CR-040: only when declared
            list.Add(new ConfigRecord(p.Code, rec));
        }
        return list;
    }, ct);

    public IReadOnlyList<FieldChange> Diff(ConfigRecord package, ConfigRecord target)
    {
        var list = GroupKit.DiffFields(package, target, HeaderFields).ToList();
        if (package.Fields.ContainsKey("productModelCode"))   // an old package has no such field: nothing to compare
        {
            var pa = ConfigPackage.Render(package.Fields["productModelCode"]); var pb = ConfigPackage.Render(target.Fields.TryGetPropertyValue("productModelCode", out var tv) ? tv : null);
            if (pa != pb) list.Add(new FieldChange("productModelCode", pb, pa));
        }
        if (GroupKit.Bool(package, "requiresReadiness") == true && !(target.Fields.ContainsKey("requiresReadiness") && GroupKit.Bool(target, "requiresReadiness") == true))
            list.Add(new FieldChange("requiresReadiness", "false", "true"));   // CR-039: one way, an import never removes the requirement
        if (package.Fields.ContainsKey("queueScope"))   // CR-040: an old package never clears a declared scope
        {
            var pa = ConfigPackage.Render(package.Fields["queueScope"]); var pb = ConfigPackage.Render(target.Fields.TryGetPropertyValue("queueScope", out var qv) ? qv : null);
            if (pa != pb) list.Add(new FieldChange("queueScope", pb, pa));
        }
        var a = Def(package); var b = Def(target);
        if (a is not null && (b is null || Canon(a) != Canon(b)))
            list.Add(new FieldChange("definition", b is null ? "(none)" : $"{b.Steps.Count} steps, first '{b.FirstStepKey}'", $"{a.Steps.Count} steps, first '{a.FirstStepKey}' (new Draft version)"));
        return list;
    }

    private static HashSet<string> Triggers(ConfigRecord r) =>
        (r.Fields["triggerLocationCodes"] as JsonArray ?? []).Select(x => x?.GetValue<string>()?.Trim() ?? "").Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> LocationCodes(ConfigRecord r)
    {
        foreach (var c in (r.Fields["triggerLocationCodes"] as JsonArray ?? []).Select(x => x?.GetValue<string>())) if (c is not null) yield return c;
        if (Def(r) is not { } d) yield break;
        foreach (var s in d.Steps)
        {
            if (s.WaitingPositionCode is { } w) yield return w;
            foreach (var c in s.Conditions ?? []) if (c.LocationCode is { } l) yield return l;
            foreach (var cand in s.Candidates ?? [])
            {
                yield return cand.LocationCode;
                foreach (var c in cand.Conditions ?? []) if (c.LocationCode is { } l) yield return l;
            }
        }
    }

    public Task<(string Code, string Message)?> CheckAsync(ConfigRecord package, ConfigRecord? target, PlanContext ctx, CancellationToken ct)
    {
        (string, string)? Result()
        {
            if (target is null && GroupKit.Bool(package, "reviewBlocked") == true)
                return ("PROCESS_REVIEW_BLOCKED_NOT_TRANSFERRABLE", $"Process {package.Key} is REVIEW-blocked in the package; an import never creates or changes the REVIEW block (BP-04), so it is not created here.");
            if (target is not null && (GroupKit.Str(package, "requestType") != GroupKit.Str(target, "requestType") || GroupKit.Str(package, "rackKind") != GroupKit.Str(target, "rackKind")))
                return ("PROCESS_HEADER_INCOMPATIBLE", $"Process {package.Key}: request type / rack kind differ ({GroupKit.Str(target, "requestType")}/{GroupKit.Str(target, "rackKind")} here); only name, description and trigger locations may change.");
            var pkgModel = package.Fields.ContainsKey("productModelCode") ? GroupKit.Str(package, "productModelCode") : null;
            if (pkgModel is not null && ctx.Target.GetValueOrDefault(ConfigGroupIds.ProductModel)?.ContainsKey(pkgModel) != true && ctx.Package.GetValueOrDefault(ConfigGroupIds.ProductModel)?.ContainsKey(pkgModel) != true)
                return ("MISSING_DEPENDENCY", $"Process {package.Key} belongs to product model {pkgModel}, which does not exist here and is not part of the selected import.");
            if (target is not null && pkgModel is not null && pkgModel != (target.Fields.ContainsKey("productModelCode") ? GroupKit.Str(target, "productModelCode") : null)
                && target.Fields["definitionSource"] is JsonObject { } src && src["state"]?.ToString() == "Published")
                return ("PROCESS_MODEL_LOCKED", $"Process {package.Key} has a Published version: its product model cannot be changed by an import.");
            if (target is not null && target.Fields["definitionSource"] is JsonObject { } current && current["state"]?.ToString() == "Published" && !Triggers(package).SetEquals(Triggers(target)))
                return ("PROCESS_HEADER_LOCKED", $"Process {package.Key} has a Published version: its trigger locations cannot be changed by an import (CR-042). Disable the published version first, or exclude the process.");
            var missing = LocationCodes(package).Where(c => !ctx.LocationExists(c)).Distinct(StringComparer.Ordinal).ToList();
            if (missing.Count > 0) return ("MISSING_DEPENDENCY", $"Process {package.Key} uses location(s) that do not exist here and are not part of the selected import: {string.Join(", ", missing)}.");
            return null;
        }
        return Task.FromResult(Result());
    }

    public async Task ApplyAsync(IReadOnlyList<(PlanItem Item, ConfigRecord Package, ConfigRecord? Target)> items, ApplyContext ctx, CancellationToken ct)
    {
        foreach (var (item, pkg, target) in items)
        {
            var triggers = (pkg.Fields["triggerLocationCodes"] as JsonArray ?? []).Select(x => x!.GetValue<string>()).ToList();
            Guid? modelId = null;   // CR-036 (A.1): absent in the package = "not given" = unchanged
            if (pkg.Fields.ContainsKey("productModelCode") && GroupKit.Str(pkg, "productModelCode") is { } pmc)
                modelId = (await uow.ExecuteAsync(async t => await models.GetByCodeAsync(pmc, t), ct))?.Id ?? throw AppException.NotFound("MODEL_NOT_FOUND", $"Product model {pmc} not found.");
            var def = Def(pkg)!;
            var source = pkg.Fields["definitionSource"] as JsonObject;
            var note = $"Imported by CR-026 ({ctx.ImportId:D}) from source version {source?["number"]} ({source?["state"]})";
            def = def with { Note = note };
            if (item.Action == PlanAction.Create)
            {
                var created = await admin.CreateAsync(new ProcessHeader(pkg.Key, GroupKit.Str(pkg, "name")!, GroupKit.Str(pkg, "description"), GroupKit.Str(pkg, "requestType")!, GroupKit.Str(pkg, "rackKind")!, triggers, modelId), ctx.Actor.Username, ct);
                await admin.SaveDefinitionAsync(created.Versions[0].Id, def, ct);   // a Draft: never validated or published here
                if (pkg.Fields.ContainsKey("requiresReadiness") && GroupKit.Bool(pkg, "requiresReadiness") == true) await admin.RequireReadinessAsync(created.Id, ct);
                if (pkg.Fields.ContainsKey("queueScope")) await admin.SetQueueScopeAsync(created.Id, GroupKit.Str(pkg, "queueScope"), $"Configuration import {ctx.ImportId:D}", ct);
                GroupKit.Audit(audit, ctx, "CONFIG_PROCESS_IMPORT_CREATE", "BusinessProcess", pkg.Key, null, new { pkg.Key, draftVersion = 1 });
                continue;
            }
            var id = await uow.ExecuteAsync(async t => (await routing.GetProcessByCodeAsync(pkg.Key, t))?.Id ?? throw AppException.NotFound("PROCESS_NOT_FOUND", $"Process {pkg.Key} not found."), ct);
            if (item.Changes.Any(c => HeaderFields.Contains(c.Field) || c.Field == "productModelCode"))
            {
                // header only: request type, rack kind and the enabled flag stay as they are here; the REVIEW block is not touched by this call
                await admin.UpdateAsync(id, new ProcessHeader(pkg.Key, GroupKit.Str(pkg, "name")!, GroupKit.Str(pkg, "description"), GroupKit.Str(target!, "requestType")!, GroupKit.Str(target!, "rackKind")!, triggers, modelId),
                    GroupKit.Bool(target!, "enabled")!.Value, ct);
                GroupKit.Audit(audit, ctx, "CONFIG_PROCESS_IMPORT_HEADER", "BusinessProcess", pkg.Key, target!.Fields.ToJsonString(), new { name = pkg.Fields["name"]?.ToString(), triggers });
            }
            if (item.Changes.Any(c => c.Field == "requiresReadiness")) await admin.RequireReadinessAsync(id, ct);
            if (item.Changes.Any(c => c.Field == "queueScope")) await admin.SetQueueScopeAsync(id, GroupKit.Str(pkg, "queueScope"), $"Configuration import {ctx.ImportId:D}", ct);
            if (item.Changes.Any(c => c.Field == "definition"))
            {
                var nv = await admin.NewVersionAsync(id, null, ctx.Actor.Username, ct);   // a new Draft; Published / Disabled versions are never edited
                await admin.SaveDefinitionAsync(nv.Id, def, ct);
                GroupKit.Audit(audit, ctx, "CONFIG_PROCESS_IMPORT_VERSION", "BusinessProcess", pkg.Key, null, new { draftVersion = nv.Number });
            }
        }
    }
}

// ====================================================================================================================== Product models (CR-036 A.1)
public sealed partial class ProductModelConfigGroup(IUnitOfWork uow, IProductModelRepository models, ProductModelService svc, IAuditLog audit) : IConfigGroup
{
    [GeneratedRegex(@"^[A-Z0-9._-]{1,40}$")] private static partial Regex CodeRegex();
    private static readonly string[] Compared = ["name", "enabled"];

    public string Id => ConfigGroupIds.ProductModel;
    public string File => "product-models.json";
    public IReadOnlyList<string> DependsOn => [];
    public bool UpdateOnly => false;

    public IReadOnlyList<string> Validate(ConfigRecord r)
    {
        var p = new List<string>();
        if (!CodeRegex().IsMatch(r.Key)) p.Add("the model code must be upper-case letters, digits, '.', '-' or '_' (max 40).");
        GroupKit.RequireString(r, "name", 200, p);
        if (GroupKit.Bool(r, "enabled") is null) p.Add("'enabled' is required.");
        return p;
    }

    public Task<IReadOnlyList<ConfigRecord>> ReadAsync(CancellationToken ct) => uow.ExecuteAsync<IReadOnlyList<ConfigRecord>>(async t =>
        (await models.ListAsync(t)).OrderBy(m => m.Code, StringComparer.Ordinal).Select(m => new ConfigRecord(m.Code, new JsonObject { ["name"] = m.Name, ["enabled"] = m.Enabled })).ToList(), ct);

    public IReadOnlyList<FieldChange> Diff(ConfigRecord package, ConfigRecord target) => GroupKit.DiffFields(package, target, Compared);

    public Task<(string Code, string Message)?> CheckAsync(ConfigRecord package, ConfigRecord? target, PlanContext ctx, CancellationToken ct) => Task.FromResult<(string, string)?>(null);

    public async Task ApplyAsync(IReadOnlyList<(PlanItem Item, ConfigRecord Package, ConfigRecord? Target)> items, ApplyContext ctx, CancellationToken ct)
    {
        foreach (var (item, pkg, target) in items)
        {
            var create = item.Action == PlanAction.Create;
            var name = GroupKit.Str(pkg, "name")!; var enabled = GroupKit.Bool(pkg, "enabled")!.Value;
            if (create) await svc.CreateAsync(pkg.Key, name, ct);
            var id = (await uow.ExecuteAsync(async t => await models.GetByCodeAsync(pkg.Key, t), ct))?.Id ?? throw AppException.NotFound("MODEL_NOT_FOUND", $"Product model {pkg.Key} not found.");
            if (create ? !enabled : item.Changes.Count > 0) await svc.UpdateAsync(id, name, enabled, ct);
            GroupKit.Audit(audit, ctx, create ? "CONFIG_MODEL_CREATE" : "CONFIG_MODEL_UPDATE", "ProductModel", pkg.Key, target?.Fields, pkg.Fields);
        }
    }
}

// ====================================================================================================================== catalog
public sealed class ConfigGroupCatalog(IEnumerable<IConfigGroup> groups)
{
    private readonly Dictionary<string, IConfigGroup> _byId = groups.ToDictionary(g => g.Id, StringComparer.Ordinal);
    public IReadOnlyCollection<IConfigGroup> All => _byId.Values;
    public IConfigGroup Get(string id) => _byId.TryGetValue(id, out var g) ? g : throw AppException.Validation("GROUP_UNKNOWN", $"Unknown configuration group '{id}'.");
    public bool Has(string id) => _byId.ContainsKey(id);
    public IReadOnlyDictionary<string, string> Files => _byId.ToDictionary(kv => kv.Key, kv => kv.Value.File);

    /// <summary>The groups in dependency order (a group after everything it depends on). Derived from the declared dependencies, not hard-coded.</summary>
    public IReadOnlyList<string> Order(IEnumerable<string> ids)
    {
        var wanted = new HashSet<string>(ids, StringComparer.Ordinal);
        var result = new List<string>(); var state = new Dictionary<string, int>();
        void Visit(string id)
        {
            if (!wanted.Contains(id) && !_byId.ContainsKey(id)) return;
            if (state.TryGetValue(id, out var s)) { if (s == 1) throw new InvalidOperationException($"Circular group dependency at {id}."); return; }
            state[id] = 1;
            foreach (var d in _byId[id].DependsOn.Concat(_byId[id].ApplyAfter)) if (wanted.Contains(d)) Visit(d);
            state[id] = 2;
            if (wanted.Contains(id)) result.Add(id);
        }
        foreach (var id in wanted.OrderBy(x => x, StringComparer.Ordinal)) Visit(id);
        return result;
    }

    /// <summary>Groups that the selected ones depend on (directly or not) and that are not selected.</summary>
    public ExportDependencyReport Dependencies(IEnumerable<string> selected)
    {
        var sel = selected.ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>(); var reasons = new Dictionary<string, IReadOnlyList<string>>();
        void Walk(string id, HashSet<string> seen)
        {
            foreach (var d in Get(id).DependsOn)
            {
                if (!seen.Add(d)) continue;
                if (!sel.Contains(d)) { if (!missing.Contains(d)) missing.Add(d); reasons[d] = (reasons.TryGetValue(d, out var r) ? r : []).Append(id).Distinct().ToList(); }
                Walk(d, seen);
            }
        }
        foreach (var id in sel) Walk(id, []);
        return new ExportDependencyReport(Order(sel), Order(missing), reasons);
    }
}
