namespace WCS.Domain.Tasks;

public class TaskHistoryEntry
{
    private TaskHistoryEntry() { }

    internal TaskHistoryEntry(Guid taskId, TaskState? from, TaskState? to, string eventName, string? detail, string source, DateTimeOffset at)
    {
        Id = Guid.NewGuid();
        TaskId = taskId;
        FromState = from;
        ToState = to;
        EventName = eventName;
        Detail = detail;
        Source = source;
        OccurredAt = at;
    }

    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public TaskState? FromState { get; private set; }
    public TaskState? ToState { get; private set; }
    public string EventName { get; private set; } = default!;
    public string? Detail { get; private set; }
    public string Source { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
}
