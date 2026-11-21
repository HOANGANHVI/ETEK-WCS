# ADR-0010 – RCS-managed dispatch (CR-028)

- **Date:** 2026-11-21 · **Status:** accepted; implemented on the simulator; real adapter blocked by missing mapping.

## Decisions
1. **One fleet, managed by the RCS.** In `RcsManaged` mode WCS does not choose a robot: the create request carries no `agvCode` and the RCS selects (RCS-2000 API V3.3, 5.2.1).
2. **WCS learns the robot from the RCS** (`queryTaskStatus.agvCode`, 5.2.15; callback `robotCode`, 5.3.1); "sent" is not "assigned".
3. **`TransportTask.AgvId` becomes nullable** (FK and history kept); the real AMR is `RobotCode`. A task without a robot is never shown or counted as an AGV's.
4. **`PER_AGV` is for Legacy dispatch only.** In RcsManaged mode there is no AGV queue / hold; an optional fleet-level `MaxInFlightTasks` limits tasks in flight. The 13 task states and `task-state-machine.md` are unchanged; the hand-over is tracked by `RcsDispatchState`.
5. **Legacy remains the default**; the mode is explicit configuration (`Rcs:Dispatch:Mode`), an invalid value stops the start.
6. **No invented protocol:** no real create-task adapter and no real callback until the vendor confirms the location / task-type mapping and the callback authentication (`docs/03_RCS_Interface/VENDOR_QUESTIONS_CR-028.md`).

## Consequences
Additive migration (one table altered; rollback requires no RcsManaged task with a null `AgvId`). Statistics by AGV stay valid for history; by robot for new tasks.
