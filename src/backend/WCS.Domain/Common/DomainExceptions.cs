using WCS.Domain.Tasks;

namespace WCS.Domain.Common;

public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class InvalidTaskTransitionException(TaskState from, TaskState to)
    : DomainException("INVALID_TASK_TRANSITION", $"Task transition {from} -> {to} is not allowed.")
{
    public TaskState From { get; } = from;
    public TaskState To { get; } = to;
}
