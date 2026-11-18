using Serilog.Core;
using Serilog.Events;
using WCS.Application.Diagnostics;

namespace WCS.Api.Logging;

/// <summary>
/// CR-021 batch B: copies the ambient <see cref="DiagnosticContext"/> frame (CorrelationId, TaskId, AgvId, PlcId, StationId, RackId) into every log event so that events of different modules that
/// belong to one unit of work carry the same ids. A property the log call already sets itself (for example {TaskId} in its own message) is never overwritten, and a null id adds nothing.
/// </summary>
public sealed class DiagnosticContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        if (DiagnosticContext.Current is not { } f) return;
        void Add(string name, string? value) { if (value is not null) logEvent.AddPropertyIfAbsent(factory.CreateProperty(name, value)); }
        Add("CorrelationId", f.CorrelationId); Add("TaskId", f.TaskId); Add("AgvId", f.AgvId); Add("PlcId", f.PlcId); Add("StationId", f.StationId); Add("RackId", f.RackId);
    }
}
