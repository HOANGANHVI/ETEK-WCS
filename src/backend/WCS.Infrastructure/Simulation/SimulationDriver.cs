using WCS.Application;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Domain.Tasks;

namespace WCS.Infrastructure.Simulation;

public sealed record SimulationStep(string? Signal, string Message, TaskDto Task);

/// <summary>
/// Plays the part of AGV/PLC/RCS: looks at a task and emits the next vendor-neutral signal a real device would produce.
/// Used by the simulator API and by tests. It reuses the normal orchestrator entry point, so rules are enforced as in production.
/// </summary>
public sealed class SimulationDriver(ITransportTaskRepository tasks, ILocationRepository locations, ITaskOrchestrator orchestrator,
    RcsSimulator rcs)
{
    public async Task<SimulationStep> AdvanceAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await tasks.GetAsync(taskId, ct) ?? throw AppException.NotFound("TASK_NOT_FOUND", "Task not found.");
        var kind = await NextSignalAsync(task, ct);
        if (kind is null) return new SimulationStep(null, $"Nothing to simulate in state {task.State}.", task.ToDto());

        if (kind == SignalKind.RcsCancelConfirmed) rcs.ConfirmCancel(task.RcsRequestCode);
        var key = $"sim:{task.Id}:{kind}:{task.CurrentLeg}";
        var result = await orchestrator.HandleSignalAsync(new TaskSignal(kind.Value, task.Id, EventKey: key,
            Source: ConfirmationSource.Simulator, User: "simulator"), ct);
        return new SimulationStep(kind.ToString(), $"{result.Outcome}: {result.Message}", result.Task ?? task.ToDto());
    }

    public async Task<IReadOnlyList<SimulationStep>> RunAsync(Guid taskId, int maxSteps = 20, CancellationToken ct = default)
    {
        var steps = new List<SimulationStep>();
        for (var i = 0; i < maxSteps; i++)
        {
            var step = await AdvanceAsync(taskId, ct);
            if (step.Signal is null) break;
            steps.Add(step);
            if (step.Task.State is nameof(TaskState.Completed) or nameof(TaskState.Failed) or nameof(TaskState.Cancelled)) break;
        }
        return steps;
    }

    private async Task<SignalKind?> NextSignalAsync(TransportTask t, CancellationToken ct)
    {
        switch (t.State)
        {
            case TaskState.Accepted: return SignalKind.AgvStarted;
            case TaskState.WaitingForPlc: return SignalKind.PlcPermissionGranted;
            case TaskState.CancelPending: return SignalKind.RcsCancelConfirmed;
            case TaskState.Executing:
                var locId = t.CurrentLeg == TaskLeg.Pick ? t.SourceLocationId : t.DestinationLocationId;
                var loc = locId is null ? null : await locations.GetAsync(locId.Value, ct);
                if (loc is { RequiresPlcPermission: true } && !t.PlcPermissionGranted) return SignalKind.PlcPermissionRequested;
                return t.CurrentLeg == TaskLeg.Pick ? SignalKind.PickConfirmed : SignalKind.DropConfirmed;
            default: return null; // Continuing / WaitingForDestination are driven by the scheduler.
        }
    }
}
