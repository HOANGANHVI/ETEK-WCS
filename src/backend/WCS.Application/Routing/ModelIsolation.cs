using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;

namespace WCS.Application.Routing;

/// <summary>CR-036: Off = behaviour exactly as before (default). Warn = violations are recorded, nothing is blocked. Enforce = the guards apply.</summary>
public enum ModelIsolationMode { Off = 0, Warn = 1, Enforce = 2 }

public sealed class ModelIsolationOptions
{
    public const string SectionName = "ModelIsolation";
    public ModelIsolationMode Mode { get; set; } = ModelIsolationMode.Off;
}

/// <summary>Where a location is used by the leg being created.</summary>
public enum LocationUse { Source = 1, Destination = 2 }

public sealed record ModelGuardFinding(string Code, string Detail);

/// <summary>Stable codes of the Product Model / Manual QC guards (CR-036).</summary>
public static class ModelCodes
{
    public const string ModelMismatch = "MODEL_MISMATCH";                       // INV-M1 / INV-M2 (runtime)
    public const string RackModelUnknown = "RACK_MODEL_UNKNOWN";                // INV-M2: the rack has no model
    public const string RackManualHandling = "RACK_MANUAL_HANDLING";            // INV-M3
    public const string ManualQcNotSource = "MANUAL_QC_NOT_SOURCE";             // INV-M4
    public const string ManualQcNotBuffer = "MANUAL_QC_NOT_BUFFER";             // INV-M4 (publication)
    public const string LegacyProcessModelLocation = "LEGACY_PROCESS_MODEL_LOCATION";   // INV-M6
    public const string ProcessModelMismatch = "PROCESS_MODEL_MISMATCH";        // INV-M1 (publication)
    public const string MismatchAtHandover = "MODEL_MISMATCH_AT_HANDOVER";
}

/// <summary>Rate limit for guard events so a waiting order does not write the same event on every scheduler tick.</summary>
public sealed class ModelGuardThrottle
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _last = new();
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    public bool ShouldEmit(string key, DateTimeOffset now)
    {
        if (_last.TryGetValue(key, out var t) && now - t < Window) return false;
        _last[key] = now; return true;
    }
}

/// <summary>
/// CR-036 (Batch 1). The single place that decides whether a location / rack may be used by a process of a given Product Model, and what is recorded when it may not.
/// It reads the mode from <c>ModelIsolation:Mode</c>. It never changes data: callers decide what to do with a violation.
/// </summary>
public sealed class ModelGuard(IOptions<ModelIsolationOptions> options)
{
    private readonly List<ModelGuardFinding> _findings = [];

    public ModelIsolationMode Mode => options.Value.Mode;
    public bool On => Mode != ModelIsolationMode.Off;
    public bool Enforcing => Mode == ModelIsolationMode.Enforce;

    /// <summary>Findings collected during the current scope (one scheduler step / request).</summary>
    public IReadOnlyList<ModelGuardFinding> Findings => _findings;
    public void ClearFindings() => _findings.Clear();
    public void Report(string code, string detail)
    {
        if (!_findings.Any(f => f.Code == code && f.Detail == detail)) _findings.Add(new ModelGuardFinding(code, detail));
    }

    /// <summary>The rack filter of an automatic source search: only when enforcing. NULL = the historical query (no filter).</summary>
    public RackSelection? SelectionFor(Guid? processModelId) =>
        Enforcing ? new RackSelection(processModelId, RequireNoModel: processModelId is null, AutoOnly: true) : null;

    /// <summary>Why a location may not be used (null = it may). Pure function of the data; does not check the mode.</summary>
    public static string? LocationViolation(Location loc, Guid? processModelId, LocationUse use)
    {
        if (processModelId is { } pm)
        {
            if (loc.ProductModelId != pm) return ModelCodes.ModelMismatch;
        }
        else if (loc.ProductModelId is not null) return ModelCodes.LegacyProcessModelLocation;
        if (use == LocationUse.Source && loc.StationRole == StationRole.ManualQcStation) return ModelCodes.ManualQcNotSource;
        return null;
    }

    /// <summary>A physical drop already happened: do the rack and the destination belong to the model of the process? (model only; disposition is not part of this check)</summary>
    public static string? HandoverViolation(Rack rack, Location destination, Guid? processModelId)
    {
        if (processModelId is { } pm) return rack.ProductModelId == pm && destination.ProductModelId == pm ? null : ModelCodes.MismatchAtHandover;
        return rack.ProductModelId is null && destination.ProductModelId is null ? null : ModelCodes.MismatchAtHandover;
    }

    /// <summary>Why a rack may not be used by an automatic process (null = it may).</summary>
    public static string? RackViolation(Rack rack, Guid? processModelId)
    {
        if (rack.Disposition == RackDisposition.ManualHandling) return ModelCodes.RackManualHandling;
        if (processModelId is { } pm)
        {
            if (rack.ProductModelId is null) return ModelCodes.RackModelUnknown;
            if (rack.ProductModelId != pm) return ModelCodes.ModelMismatch;
        }
        else if (rack.ProductModelId is not null) return ModelCodes.LegacyProcessModelLocation;
        return null;
    }
}

/// <summary>The model of the process whose leg is being evaluated (carried into the condition evaluator).</summary>
public sealed record ModelContext(Guid? ProcessModelId);

/// <summary>
/// CR-036: the ONE place that applies the Manual QC rule when a rack is placed at a location (route drop, legacy drop, reconciliation, new stock).
/// A rack placed at a location with <see cref="StationRole.ManualQcStation"/> gets <see cref="RackDisposition.ManualHandling"/>; nothing here (or anywhere) sets it back.
/// With <c>ModelIsolation:Mode = Off</c> nothing changes.
/// </summary>
public sealed class RackPlacement(IOptions<ModelIsolationOptions> options)
{
    public bool Active => options.Value.Mode != ModelIsolationMode.Off;

    /// <summary>Returns the audit record to add (same unit of work as the placement), or null when nothing changed.</summary>
    public InventoryTransaction? OnPlaced(Rack rack, Location location, Guid? taskId, ConfirmationSource source, string? user, DateTimeOffset now)
    {
        if (!Active || location.StationRole != StationRole.ManualQcStation) return null;
        if (!rack.MarkManualHandling()) return null;
        return new InventoryTransaction(InventoryEventType.DispositionChanged, rack.Id, null, location.Id, taskId, rack.Status, rack.Status, source, user,
            BindOutcome.NotRequested, $"Manual handling at {location.Code}: the rack left the automatic flow (one way)", now);
    }
}
