using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Domain.Common;
using WCS.Domain.Locations;
using WCS.Domain.Routing;

namespace WCS.Application.Routing;

public enum IssueSeverity { Warning = 1, Error = 2 }

public sealed record ProcessIssue(IssueSeverity Severity, string Code, string Message, string? StepKey = null);

public sealed record ValidationOutcome(IReadOnlyList<ProcessIssue> Issues)
{
    public bool IsValid => Issues.All(i => i.Severity != IssueSeverity.Error);
}

/// <summary>
/// Publish gate (CR-010 3.5). A process version is published only when every check passes; the Admin sees each failure with its step.
/// The rules that protect approved behaviour (REVIEW flows, protected locations, approved PLC mapping) are NOT switchable.
/// </summary>
public sealed class ProcessValidator(ILocationRepository locations, IRoutingRepository routing, IOptions<RouteEngineOptions> options, IOptions<ModelIsolationOptions> modelOptions)
{
    private static readonly HashSet<string> InventoryStates = new(StringComparer.OrdinalIgnoreCase) { "Empty", "Occupied", "HasFreeSlot", "NotUnknown" };
    private static readonly HashSet<string> BindingStates = new(StringComparer.OrdinalIgnoreCase) { "Bound", "Unbound" };

    public async Task<ValidationOutcome> ValidateAsync(BusinessProcess process, BusinessProcessVersion version, CancellationToken ct = default)
    {
        var issues = new List<ProcessIssue>();
        void Err(string code, string msg, string? step = null) => issues.Add(new ProcessIssue(IssueSeverity.Error, code, msg, step));
        void Warn(string code, string msg, string? step = null) => issues.Add(new ProcessIssue(IssueSeverity.Warning, code, msg, step));

        // CR-036 publication rules (INV-M1, INV-M4, INV-M6). Off = none; Warn = warnings; Enforce = errors. These are PUBLICATION errors: they are not Operational Readiness.
        var modelMode = modelOptions.Value.Mode;
        var modelChecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void ModelIssue(string code, string msg, string? step)
        {
            if (modelMode == ModelIsolationMode.Enforce) Err(code, msg, step); else if (modelMode == ModelIsolationMode.Warn) Warn(code, msg, step);
        }

        if (process.ReviewBlocked) Err("PROCESS_REVIEW_BLOCKED", $"Process {process.Code} is REVIEW and cannot be published. {process.ReviewReason}");
        if (process.TriggerLocationCodes.Count == 0) Err("NO_TRIGGER_LOCATION", "The process has no location that can start it.");

        var steps = version.Steps.OrderBy(s => s.Order).ToList();
        if (steps.Count == 0) { Err("NO_STEPS", "The version has no step."); return new ValidationOutcome(issues); }
        var byKey = new Dictionary<string, RouteStep>(StringComparer.Ordinal);
        foreach (var s in steps)
            if (!byKey.TryAdd(s.StepKey, s)) Err("DUPLICATE_STEP_KEY", $"Step key '{s.StepKey}' is used more than once.", s.StepKey);
        if (!byKey.ContainsKey(version.FirstStepKey)) { Err("FIRST_STEP_MISSING", $"First step '{version.FirstStepKey}' does not exist."); return new ValidationOutcome(issues); }

        // ---------------------------------------------------------------- locations
        var locationCache = new Dictionary<string, Location?>(StringComparer.OrdinalIgnoreCase);
        async Task<Location?> Loc(string code) =>
            locationCache.TryGetValue(code, out var l) ? l : locationCache[code] = await locations.GetByCodeAsync(code, ct);
        var protectedCodes = new HashSet<string>(options.Value.ProtectedLocationCodes, StringComparer.OrdinalIgnoreCase);
        var checkedPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task UseLocation(string code, string? step, string role)
        {
            if (protectedCodes.Contains(code)) Err("LOCATION_PROTECTED", $"Location {code} keeps its legacy process (UNCHANGED) and cannot be used by a configurable route.", step);
            var loc = await Loc(code);
            if (loc is null) { Err("LOCATION_NOT_FOUND", $"{role} location '{code}' does not exist.", step); return; }
            if (!loc.Enabled) Err("LOCATION_DISABLED", $"{role} location '{code}' is disabled.", step);
            if (modelMode != ModelIsolationMode.Off && modelChecked.Add(code))
            {
                if (process.ProductModelId is { } pm) { if (loc.ProductModelId != pm) ModelIssue(ModelCodes.ProcessModelMismatch, $"{role} location '{code}' does not belong to the product model of process {process.Code}.", step); }
                else if (loc.ProductModelId is not null) ModelIssue(ModelCodes.LegacyProcessModelLocation, $"{role} location '{code}' belongs to a product model, but process {process.Code} has none (a process without a model never uses a modelled location).", step);
            }
            if (!checkedPorts.Add(code)) return;
            var port = await routing.FindPortByLocationAsync(code, ct);
            if (port is null) return;
            if (!port.Enabled) Err("PORT_DISABLED", $"Port {port.Id} of location '{code}' is disabled.", step);
            else if (!await routing.IsPlcEnabledAsync(port.PlcId, ct)) Err("PLC_DISABLED", $"PLC {port.PlcId} serving location '{code}' is disabled.", step);
            else if (!await routing.IsPortMappingApprovedAsync(port.Id, ct))
                Err("PORT_MAPPING_NOT_APPROVED", $"The IO mapping of port {port.Id} (location '{code}') is not approved by the PLC engineer; no physical operation may use it.", step);
        }

        foreach (var t in process.TriggerLocationCodes) await UseLocation(t, null, "Trigger");

        // ---------------------------------------------------------------- per step
        foreach (var s in steps)
        {
            void CheckLink(string? target, string what)
            {
                if (target is not null && !byKey.ContainsKey(target)) Err("STEP_LINK_INVALID", $"{what} step '{target}' does not exist.", s.StepKey);
            }
            CheckLink(s.NextStepKey, "Next"); CheckLink(s.AlternativeNextStepKey, "Alternative next");

            switch (s.Type)
            {
                case RouteStepType.ProcessCompletion:
                    if (s.NextStepKey is not null || s.AlternativeNextStepKey is not null) Err("COMPLETION_HAS_NEXT", "A completion step cannot have a next step.", s.StepKey);
                    break;
                case RouteStepType.Condition:
                    if (s.NextStepKey is null) Err("CONDITION_NO_NEXT", "A condition step needs a next step (condition true).", s.StepKey);
                    if (s.AlternativeNextStepKey is null) Err("CONDITION_NO_ALTERNATIVE", "A condition step needs an alternative next step (condition false).", s.StepKey);
                    break;
                case RouteStepType.Waiting:
                    if (s.NextStepKey is null) Err("WAITING_NO_NEXT", "A waiting step needs a next step.", s.StepKey);
                    break;
                default:
                    if (s.NextStepKey is null) Err("STEP_NO_NEXT", "A movement step needs a next step.", s.StepKey);
                    break;
            }
            if (s.WaitBehavior == WaitBehavior.Alternative && s.AlternativeNextStepKey is null)
                Err("WAIT_ALTERNATIVE_MISSING", "WaitBehavior=Alternative needs an alternative next step.", s.StepKey);

            // conditions (supported types only; parameters checked)
            IReadOnlyList<ConditionSpec> own;
            try { own = s.Conditions; } catch (DomainException e) { Err(e.Code, e.Message, s.StepKey); own = []; }
            foreach (var c in own)
            {
                if (s.Type is RouteStepType.Condition or RouteStepType.Waiting && string.IsNullOrWhiteSpace(c.LocationCode))
                    Err("CONDITION_LOCATION_REQUIRED", $"Condition {c.Type} of a {s.Type} step needs an explicit location.", s.StepKey);
                CheckCondition(c, s.StepKey, Err);
                if (c.LocationCode is { } cl) await UseLocation(cl, s.StepKey, "Condition");
            }
            if (s.Type is RouteStepType.Condition or RouteStepType.Waiting && own.Count == 0)
                Err("CONDITIONS_REQUIRED", $"A {s.Type} step needs at least one condition.", s.StepKey);
            if (s.WaitingPositionCode is { } wp)
            {
                var wl = await Loc(wp);
                if (wl is null) Err("LOCATION_NOT_FOUND", $"Waiting position '{wp}' does not exist.", s.StepKey);
                else if (wl.Type != LocationType.WaitingPosition) Err("WAITING_POSITION_TYPE", $"'{wp}' is not a waiting position; a waiting position is never a storage location.", s.StepKey);
            }

            if (!s.IsMovement) { if (s.Candidates.Count > 0) Warn("CANDIDATES_IGNORED", "Candidates of a non-movement step are ignored.", s.StepKey); continue; }

            // movement steps
            if (s.Type == RouteStepType.Pickup && s.SourceMode == SourceMode.PreviousDestination) Err("SOURCE_MODE_INVALID", "A pickup step cannot start from the previous destination.", s.StepKey);
            if (s.SourceMode == SourceMode.PreviousDestination && s.StepKey == version.FirstStepKey)
                Err("PREVIOUS_DESTINATION_ON_FIRST", "The first step has no previous destination.", s.StepKey);
            var sources = s.Candidates.Where(c => c.Role == CandidateRole.Source).ToList();
            var dests = s.Candidates.Where(c => c.Role == CandidateRole.Destination).ToList();
            if (modelMode != ModelIsolationMode.Off && s.SourceMode == SourceMode.Request)
                foreach (var tc in process.TriggerLocationCodes)
                    if (await Loc(tc) is { StationRole: StationRole.ManualQcStation }) ModelIssue(ModelCodes.ManualQcNotSource, $"The request location '{tc}' is a manual QC station and is the source of this step: a manual QC station is never the source of an automatic process.", s.StepKey);
            if (s.SourceMode == SourceMode.Candidates && sources.Count == 0) Err("NO_SOURCE_CANDIDATE", "SourceMode=Candidates needs at least one source candidate.", s.StepKey);
            if (s.SourceMode != SourceMode.Candidates && sources.Count > 0) Warn("SOURCE_CANDIDATES_IGNORED", "Source candidates are ignored unless SourceMode=Candidates.", s.StepKey);
            if (s.DestinationMode == DestinationMode.Candidates && dests.Count == 0) Err("NO_DESTINATION_CANDIDATE", "DestinationMode=Candidates needs at least one destination candidate.", s.StepKey);
            if (s.DestinationMode == DestinationMode.Request && dests.Count > 0) Warn("DEST_CANDIDATES_IGNORED", "Destination candidates are ignored unless DestinationMode=Candidates.", s.StepKey);
            if (s.SourceMode == SourceMode.Request && s.DestinationMode == DestinationMode.Request) Err("SAME_ENDS", "Source and destination cannot both be the request location.", s.StepKey);
            if (s.Resolution == DestinationResolution.AfterPick && s.WaitBehavior == WaitBehavior.Alternative)
                Err("ALTERNATIVE_AFTER_PICK", "An alternative step cannot replace the destination once the rack is on the AMR; use Wait or Fail.", s.StepKey);

            foreach (var grp in s.Candidates.GroupBy(c => (c.Role, c.Priority, c.Order)).Where(g => g.Count() > 1))
                Err("DUPLICATE_CANDIDATE_ORDER", $"Two {grp.Key.Role} candidates share priority {grp.Key.Priority} and order {grp.Key.Order}.", s.StepKey);

            foreach (var c in s.Candidates)
            {
                await UseLocation(c.LocationCode, s.StepKey, c.Role.ToString());
                IReadOnlyList<ConditionSpec> cc;
                try { cc = c.Conditions; } catch (DomainException e) { Err(e.Code, e.Message, s.StepKey); cc = []; }
                foreach (var cond in cc)
                {
                    CheckCondition(cond, s.StepKey, Err);
                    if (cond.LocationCode is { } cl) await UseLocation(cl, s.StepKey, "Condition");
                }
                var loc = await Loc(c.LocationCode);
                if (loc is null) continue;
                if (modelMode != ModelIsolationMode.Off && loc.StationRole == StationRole.ManualQcStation)
                {
                    if (s.Type is RouteStepType.BufferStorage or RouteStepType.BufferRetrieval)
                        ModelIssue(ModelCodes.ManualQcNotBuffer, $"'{c.LocationCode}' is a manual QC station, not a buffer: it cannot be used by a {s.Type} step.", s.StepKey);
                    if (c.Role == CandidateRole.Source)
                        ModelIssue(ModelCodes.ManualQcNotSource, $"'{c.LocationCode}' is a manual QC station and cannot be the source of an automatic process.", s.StepKey);
                }
                if (c.Role == CandidateRole.Destination)
                {
                    if (!RouteConditionEvaluator.CanStoreRack(loc.Type)) Err("DESTINATION_NOT_STORAGE", $"'{c.LocationCode}' ({loc.Type}) cannot store a rack and cannot be a destination.", s.StepKey);
                    if (!loc.Accepts(process.RackKind)) Err("RACK_TYPE_MISMATCH", $"Destination '{c.LocationCode}' does not accept {process.RackKind} racks.", s.StepKey);
                    if (s.Type == RouteStepType.BufferStorage && loc.Type is not (LocationType.Buffer or LocationType.ManualBuffer))
                        Err("BUFFER_NOT_ALLOWED", $"Destination '{c.LocationCode}' of a BufferStorage step must be a buffer.", s.StepKey);
                }
                else
                {
                    if (!RouteConditionEvaluator.CanStoreRack(loc.Type)) Err("SOURCE_NOT_STORAGE", $"'{c.LocationCode}' ({loc.Type}) cannot hold a rack and cannot be a source.", s.StepKey);
                    if (!loc.Accepts(process.RackKind)) Err("RACK_TYPE_MISMATCH", $"Source '{c.LocationCode}' does not hold {process.RackKind} racks.", s.StepKey);
                    if (s.Type == RouteStepType.BufferRetrieval && loc.Type is not (LocationType.Buffer or LocationType.ManualBuffer))
                        Err("BUFFER_NOT_ALLOWED", $"Source '{c.LocationCode}' of a BufferRetrieval step must be a buffer.", s.StepKey);
                }
            }
        }

        // ---------------------------------------------------------------- graph: orphans, cycles, completion reachability
        var edges = steps.ToDictionary(s => s.StepKey, s => new[] { s.NextStepKey, s.AlternativeNextStepKey }.Where(x => x is not null && byKey.ContainsKey(x!)).Select(x => x!).Distinct().ToList());
        var reachable = new HashSet<string>(); var stack = new Stack<string>(); stack.Push(version.FirstStepKey);
        while (stack.Count > 0) { var k = stack.Pop(); if (!reachable.Add(k)) continue; foreach (var n in edges[k]) stack.Push(n); }
        foreach (var s in steps.Where(s => !reachable.Contains(s.StepKey))) Err("STEP_ORPHAN", $"Step '{s.StepKey}' cannot be reached from the first step.", s.StepKey);

        var color = new Dictionary<string, int>();   // 0 new, 1 in progress, 2 done
        bool cycle = false;
        void Dfs(string k)
        {
            color[k] = 1;
            foreach (var n in edges[k])
            {
                if (color.GetValueOrDefault(n) == 1) { cycle = true; Err("ROUTE_CYCLE", $"The route loops back from '{k}' to '{n}' (an endless loop).", k); }
                else if (color.GetValueOrDefault(n) == 0) Dfs(n);
            }
            color[k] = 2;
        }
        Dfs(version.FirstStepKey);

        if (!cycle)
        {
            var canFinish = new Dictionary<string, bool>();
            bool Finish(string k) => canFinish.TryGetValue(k, out var v) ? v : canFinish[k] = byKey[k].Type == RouteStepType.ProcessCompletion || edges[k].Any(Finish);
            foreach (var s in steps.Where(s => reachable.Contains(s.StepKey) && !Finish(s.StepKey)))
                Err("NO_PATH_TO_COMPLETION", $"No branch from step '{s.StepKey}' leads to a process completion.", s.StepKey);
        }
        if (!steps.Any(s => s.Type == RouteStepType.ProcessCompletion)) Err("NO_COMPLETION_STEP", "The process has no ProcessCompletion step.");

        // ambiguity (warning): another published process answers the same request
        foreach (var t in process.TriggerLocationCodes)
        {
            var others = (await routing.FindPublishedAsync(process.RequestType, process.RackKind, t, ct)).Where(x => x.Process.Id != process.Id).ToList();
            if (others.Count > 0) Warn("AMBIGUOUS_TRIGGER", $"Process {others[0].Process.Code} is already published for the same request at {t}; a request without an explicit process code will be refused.");
        }
        return new ValidationOutcome(issues);
    }

    private static void CheckCondition(ConditionSpec c, string step, Action<string, string, string?> err)
    {
        if (!Enum.IsDefined(c.Type)) { err("CONDITION_UNSUPPORTED", $"Condition type {c.Type} is not supported.", step); return; }
        switch (c.Type)
        {
            case ConditionType.RackTypeMatch when string.IsNullOrWhiteSpace(c.Parameter):
                err("CONDITION_PARAMETER", "RackTypeMatch needs a parameter (Full, Empty or a rack type).", step); break;
            case ConditionType.InventoryState when !InventoryStates.Contains(c.Parameter ?? ""):
                err("CONDITION_PARAMETER", "InventoryState parameter must be Empty, Occupied, HasFreeSlot or NotUnknown.", step); break;
            case ConditionType.RackBinding when !BindingStates.Contains(c.Parameter ?? ""):
                err("CONDITION_PARAMETER", "RackBinding parameter must be Bound or Unbound.", step); break;
        }
    }
}
