namespace WCS.Domain.Tasks;

public class TaskHistoryEntry
{
    private TaskHistoryEntry() { }

    internal TaskHistoryEntry(Guid taskId, TaskState? from, TaskState? to, string eventName, string? detail, string source, DateTimeOffset at, HistoryDetail? code = null)
    {
        Id = Guid.NewGuid();
        TaskId = taskId;
        FromState = from;
        ToState = to;
        EventName = eventName;
        Detail = detail;
        DetailCode = code?.Code; DetailArgsJson = code?.ArgsJson;
        Source = source;
        OccurredAt = at;
    }

    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public TaskState? FromState { get; private set; }
    public TaskState? ToState { get; private set; }
    public string EventName { get; private set; } = default!;
    /// <summary>The original English text (raw, kept for diagnostics and audit).</summary>
    public string? Detail { get; private set; }
    /// <summary>Stable code of the detail (null for entries written before codes existed or whose text is a raw external message) and its parameters as JSON.</summary>
    public string? DetailCode { get; private set; }
    public string? DetailArgsJson { get; private set; }
    public string Source { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
}
