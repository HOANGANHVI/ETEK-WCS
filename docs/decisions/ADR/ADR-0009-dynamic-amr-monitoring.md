# ADR-0009 – Dynamic AMR monitoring from the RCS (CR-027)

- **Date:** 2026-11-19 · **Status:** Phase 1 accepted and implemented; **Phase 2 deferred, not approved**.

## Context
The system assumed two AMRs (AGV1 full racks, AGV2 empty racks) in the database, the task assignment and the screens. In Real mode the RCS owns the fleet.

## Decisions
1. **RCS is the source of the AMR fleet in Real mode.** WCS only queries (`queryAgvStatus`, RCS-2000 API V3.3, 5.2.14) and monitors; it neither chooses nor creates AMRs.
2. **Phase 1 changes only monitoring.** The fleet is a separate read model (`rcs_amr_snapshots`, `rcs_fleet_state`), keyed by source and `robotCode`; the `agvs` table is not written.
3. **Simulator and Real data are stored and shown apart**; a simulated list is always labelled.
4. **No invented data:** a failed query never adds or removes an AMR; kept data is shown as stale, an AMR absent from a successful answer is shown as missing and kept.
5. **Reuse the existing loop** (`AgvStatusHostedService`); the interval is configurable (default 5 s).
6. **Unchanged:** `TransportTask.AgvId` and its foreign key, `PER_AGV`, AGV1/AGV2 roles, `TaskOrchestrator`, `RouteEngine`, `DispatchHold`, `genAgvSchedulingTask` / `RcsCreateTaskRequest.RobotCode`, PLC, Station, Inventory, Rack binding, Dashboard, KPI, statistics, history.
7. **Phase 2 (RCS chooses the AMR at task creation) is deferred.** It needs an owner decision on how `PER_AGV` is expressed without a WCS-chosen AMR, on `AgvId` (nullable / filled from `queryTaskStatus` / `agvCallback`) and on the statistics that group by `agvId`. None of that was started.

## Consequences
New additive migration; no change to any existing table. Real-source behaviour is untested against a real RCS (see CR-027 section 7).
