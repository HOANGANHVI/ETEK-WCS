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
    RcsSimulator rcs, IRcsMessageRecorder recorder)
{
    public async Task<SimulationStep> AdvanceAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await tasks.GetAsync(taskId, ct) ?? throw AppException.NotFound("TASK_NOT_FOUND", "Task not found.");
        // CR-038: the simulator plays the part of the AGV for SIMULATOR tasks only. A task created in Real mode (or before CR-038) is never driven: its events come from the real RCS / PLC.
        if (task.ExecutionMode != WCS.Domain.Rcs.ExecutionMode.Simulator)
            throw AppException.Rejected("SIMULATION_NOT_ALLOWED_FOR_TASK", "Only a task created in Simulator mode can be driven by the simulator.");
        var kind = await NextSignalAsync(task, ct);
        if (kind is null) return new SimulationStep(null, $"Nothing to simulate in state {task.State}.", task.ToDto());

        if (kind == SignalKind.RcsCancelConfirmed) rcs.ConfirmCancel(task.RcsRequestCode);
        var key = $"sim:{task.Id}:{kind}:{task.CurrentLeg}";
        // CR-028: the RCS callback names the robot it chose (agvCallback robotCode); a Legacy task never sends one (unchanged behaviour)
        var robot = task.IsRcsManaged ? rcs.RobotOf(task.RcsRequestCode) : null;
        var result = await orchestrator.HandleSignalAsync(new TaskSignal(kind.Value, task.Id, RobotCode: robot, EventKey: key,
            Source: ConfirmationSource.Simulator, User: "simulator"), ct);
        // the simulated device event, as an IN message from the (simulated) RCS / AGV: only for events that belong to the RCS side; PLC permission events are not RCS traffic
        if (kind is SignalKind.AgvStarted or SignalKind.PickConfirmed or SignalKind.DropConfirmed or SignalKind.RcsCancelConfirmed)
            await recorder.RecordAsync(new RcsMessageEntry(WCS.Domain.Rcs.ExecutionMode.Simulator, WCS.Domain.Rcs.RcsMessageDirection.In, $"Simulated AGV Event: {kind}", "SIMULATOR (in-process, no network)", task.RcsRequestCode,
                Payload: System.Text.Json.JsonSerializer.Serialize(new { signal = kind.ToString(), taskCode = task.RcsRequestCode, robotCode = robot, taskState = result.Task?.State }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
                Result: result.Outcome.ToString()), ct);
        if (task.IsRcsManaged && result.Task?.State is nameof(TaskState.Completed) or nameof(TaskState.Cancelled)) rcs.SetStatus(task.RcsRequestCode, result.Task.State == nameof(TaskState.Completed) ? RcsTaskStatus.Completed : RcsTaskStatus.Cancelled);   // frees the simulated robot
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
