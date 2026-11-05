using WCS.Domain.Common;
using WCS.Domain.Tasks;

namespace WCS.Tests.Domain;

public class TaskStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);

    private static TransportTask NewTask() =>
        TransportTask.Create("req-1", ProcessFlow.Bp01, RackKind.Full, Guid.NewGuid(), Guid.NewGuid(), "test", Now);

    [Fact]
    public void Exactly_the_13_states_of_the_specification_exist()
    {
        var expected = new[]
        {
            "Pending", "Validating", "Queued", "Dispatching", "Accepted", "Executing", "WaitingForPlc",
            "WaitingForDestination", "Continuing", "Completed", "Failed", "CancelPending", "Cancelled",
        };
        Assert.Equal(expected.Order(), Enum.GetNames<TaskState>().Order());
    }

    [Fact]
    public void Agv_occupying_states_match_task_state_machine_3_3()
    {
        var expected = new[]
        {
            TaskState.Dispatching, TaskState.Accepted, TaskState.Executing, TaskState.WaitingForPlc,
            TaskState.WaitingForDestination, TaskState.Continuing, TaskState.CancelPending,
        };
        Assert.Equal(expected.Order(), TaskStateMachine.AgvOccupyingStates.Order());
        Assert.False(TaskStateMachine.OccupiesAgv(TaskState.Pending));
        Assert.False(TaskStateMachine.OccupiesAgv(TaskState.Queued));
    }

    [Theory]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Failed)]
    [InlineData(TaskState.Cancelled)]
    public void Terminal_states_have_no_outgoing_transitions_and_do_not_occupy_an_agv(TaskState s)
    {
        Assert.True(TaskStateMachine.IsTerminal(s));
        Assert.Empty(TaskStateMachine.NextStates(s));
        Assert.False(TaskStateMachine.OccupiesAgv(s));
    }

    [Fact]
    public void Every_non_terminal_state_can_reach_a_terminal_state()
    {
        foreach (var start in Enum.GetValues<TaskState>().Where(s => !TaskStateMachine.IsTerminal(s)))
        {
            var seen = new HashSet<TaskState> { start };
            var queue = new Queue<TaskState>([start]);
            var reachesTerminal = false;
            while (queue.Count > 0 && !reachesTerminal)
            {
                var cur = queue.Dequeue();
                foreach (var next in TaskStateMachine.NextStates(cur))
                {
                    if (TaskStateMachine.IsTerminal(next)) { reachesTerminal = true; break; }
                    if (seen.Add(next)) queue.Enqueue(next);
                }
            }
            Assert.True(reachesTerminal, $"{start} cannot reach a terminal state");
        }
    }

    [Theory]
    [InlineData(TaskState.Pending, TaskState.Executing)]
    [InlineData(TaskState.Pending, TaskState.Completed)]
    [InlineData(TaskState.Queued, TaskState.Accepted)]
    [InlineData(TaskState.Dispatching, TaskState.Completed)]
    [InlineData(TaskState.Accepted, TaskState.Continuing)]
    [InlineData(TaskState.WaitingForDestination, TaskState.Completed)]
    [InlineData(TaskState.Completed, TaskState.Executing)]
    [InlineData(TaskState.Failed, TaskState.Queued)]
    [InlineData(TaskState.Cancelled, TaskState.Pending)]
    public void Arbitrary_transitions_are_rejected(TaskState from, TaskState to)
    {
        Assert.False(TaskStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void Task_cannot_be_moved_by_an_invalid_transition_and_state_is_unchanged()
    {
        var t = NewTask();
        var ex = Assert.Throws<InvalidTaskTransitionException>(() => t.TransitionTo(TaskState.Executing, "x", "test", Now));
        Assert.Equal(TaskState.Pending, ex.From);
        Assert.Equal(TaskState.Pending, t.State);
    }

    [Fact]
    public void Happy_path_records_history_for_every_change()
    {
        var t = NewTask();
        foreach (var s in new[] { TaskState.Validating, TaskState.Dispatching, TaskState.Accepted, TaskState.Executing,
                     TaskState.Continuing, TaskState.Executing, TaskState.Completed })
            t.TransitionTo(s, $"to {s}", "test", Now);

        Assert.True(t.IsTerminal);
        Assert.NotNull(t.FinishedAt);
        Assert.Equal(8, t.History.Count); // creation + 7 transitions
        Assert.Contains(t.History, h => h.FromState == TaskState.Executing && h.ToState == TaskState.Completed);
    }

    [Fact]
    public void Drop_cannot_be_confirmed_before_pick_and_pick_only_once()
    {
        var t = NewTask();
        Assert.Throws<DomainException>(() => t.ConfirmDrop(Now));
        t.ConfirmPick(Now);
        Assert.Throws<DomainException>(() => t.ConfirmPick(Now));
        t.ConfirmDrop(Now);
        Assert.Throws<DomainException>(() => t.ConfirmDrop(Now));
    }

    [Fact]
    public void Request_key_is_mandatory_for_idempotency()
    {
        Assert.Throws<DomainException>(() =>
            TransportTask.Create(" ", ProcessFlow.Bp01, RackKind.Full, Guid.NewGuid(), Guid.NewGuid(), null, Now));
    }

    [Fact]
    public void Process_flow_enum_does_not_contain_bp04()
    {
        Assert.DoesNotContain(Enum.GetNames<ProcessFlow>(), n => n.Contains("04", StringComparison.Ordinal) || n.Contains("4"));
        Assert.Equal(5, Enum.GetValues<ProcessFlow>().Length);
    }
}
