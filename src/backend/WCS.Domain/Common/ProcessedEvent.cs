namespace WCS.Domain.Common;

/// <summary>Idempotency ledger for externally originated events (RCS callbacks, PLC events). (Source, EventKey) is unique.</summary>
public class ProcessedEvent
{
    private ProcessedEvent() { }

    public ProcessedEvent(string source, string eventKey, Guid? taskId, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); Source = source; EventKey = eventKey; TaskId = taskId; ProcessedAt = now;
    }

    public Guid Id { get; private set; }
    public string Source { get; private set; } = default!;
    public string EventKey { get; private set; } = default!;
    public Guid? TaskId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }
}
