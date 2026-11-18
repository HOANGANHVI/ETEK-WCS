# ADR-0008 – Configurable Business Process, Route Engine, multi-leg Transport Task and per-port Station Transaction (CR-010)

- **Status:** Accepted for implementation by the project owner on 2026-11-18 (implementation task "CR-010"); open items are listed in section 9.
- **Supersedes in part:** the "one transaction per PLC / one IO set per PLC" reading of the draft PLC specification (CR-010 specification step of 2026-11-17). Does **not** supersede CR-002 / ADR-0004: the 4 PLCs / 16 Station IO sets stay the baseline and the topology stays data.
- **Protected and unchanged:** `TaskStateMachine`, `InventoryService`, `PER_AGV`, BP-04 `REVIEW`, A3/A4 and D1/D2 `UNCHANGED`, WCS owns inventory, PLC IP/port/protocol settings, approved IO mappings, authentication and role permissions, the AGV task execution contract (`TransportTask`, `ITaskOrchestrator`).

## 1. Context

The approved flows (BP-01, 02, 03, 05, 06) were configured through `ProcessFlows` options and executed by `TaskOrchestrator`, which models a single pick and a single drop. The owner now requires multi-leg transport (a rack may be stored at a buffer and continue later), a port-level transaction that is independent of the Transport Task, any number of PLCs, and routes that an Admin can change without a code change.

## 2. Decisions

1. **Two levels, kept apart.**
   - *Transport Task* (code name `TransportOrder`, because `TransportTask` already exists and keeps its meaning of "AGV execution task" with its protected state machine) owns a list of *Legs*. It is completed only when the process reaches its completion step.
   - *Station Transaction* is one entry of an AMR into one port (a PLC Station of CR-002). It completes only after the handover **and** the AMR leaving the port. Its completion never completes the Transport Task.
2. **Five state families, never merged:** Transport Task (`OrderState`), Leg (`LegState`), Station Transaction (`StationTransactionState`), AGV execution (existing, unchanged), Rack/Inventory (existing, unchanged), plus the configuration state of a process version (`ProcessConfigState`).
3. **Port = `PlcStation`.** A port is the existing PLC Station linked to a location through `LocationCode`. No new port table. A location without a port needs no handshake (buffers).
4. **One active Station Transaction per port, enforced by the database** (partial unique index on active states) and by application checks. No PLC-wide lock and no system-wide lock exist; two ports of one PLC or of different PLCs run independently.
5. **Business Process = data.** `business_processes`, `business_process_versions` (Draft → Validated → Published → Disabled, exactly one Published per process), `route_steps`, `route_candidates`. A step is one of Pickup, Delivery, BufferStorage, BufferRetrieval, Waiting, Condition, ProcessCompletion. Candidates carry a priority and controlled conditions (a fixed enum, never script/SQL). The engine has no location name in code.
6. **Snapshot.** Every Transport Task stores the process id, version number and a JSON snapshot of the version. A running task never reads the live configuration, so publishing a new version cannot change it.
7. **Publish gate.** A version is publishable only if validation passes at publish time: locations exist and are enabled, rack types fit, steps link, every branch can reach completion, no loop, no orphan step, buffer steps use buffers, conditions are supported, ports have an enabled PLC **and an approved IO mapping**, the process is not REVIEW-blocked and uses no protected location (`RouteEngine:ProtectedLocationCodes`, data).
8. **Inventory by confirmed physical events only.** A Leg reserves its source rack and destination slot (same tables and rules as before: unique active source-rack reservation, location row version). `PICK_CONFIRMED` makes the rack IN_TRANSIT; `DROP_CONFIRMED` places it at the destination (a buffer is a real storage location). A waiting position is never a storage location. If a PLC report and WCS inventory disagree, the request goes to reconciliation: no overwrite, no substitute position, no dispatch.
9. **PER_AGV at leg level.** A leg holds an AGV while InProgress, or Waiting while the AMR still holds the rack. A database partial unique index allows one such leg per AGV; the engine also refuses to dispatch while a legacy `TransportTask` occupies the AGV or the AGV is on dispatch hold. The AGV is free between two legs (see open item O-1).
10. **Recovery without memory.** Everything lives in the database. `RoutingHostedService` re-evaluates every open Transport Task each tick (idempotent), restarts accepted-but-unstarted PLC requests, suspends transactions of a PLC that went offline (state kept, nothing cancelled or reset) and on reconnect reconciles with the PLC's real state through `HandshakeRecovery` (pure decision table; "no data" or "cannot prove" never guesses).
11. **No PLC write is issued by the engine.** M9101/M9120/M9121/M9122/M9123 are not written; their timing and reset ownership are open. Reading D1500/D1510/D1512/D1514/D1520 is modelled by `PlcRequestSnapshot` and validated by `PlcRequestService`; live reading is not wired (see section 9).
12. **Dispatch adapter.** `ILegDispatcher` has only a simulator implementation guarded by `IRcsDispatchGuard`; a real RCS adapter for configurable legs is not provided because the RCS task template for it is not confirmed.

## 3. Consequences

- Additive migration `AddRoutingEngine` (8 new tables, no change or drop of existing tables).
- `PlcStation`, `StationIoPoint`, `PlcConnection` unchanged; PLC count is data (`PlcProvisioningService`).
- A destination port's transaction is opened when the leg is dispatched (the port is busy from then until the AMR has left), which is more conservative than the physical entry (O-3).
- The legacy `ProcessFlows` configuration and `TaskOrchestrator` remain untouched and usable; the new engine is an additional path.

## 4. Rejected alternatives

- Extending `TaskStateMachine` / `TransportTask` for legs: protected, and it would couple route data to a fixed enum (`ProcessFlow`).
- One lock per PLC: contradicts the owner's rule and serialises independent ports.
- Free-text/scripted conditions: unsafe; conditions are a closed set with parameters.

## 5. Safety rules that are not configurable

REVIEW-blocked processes cannot be published; protected locations cannot be used; an unapproved IO mapping blocks publishing; the engine cannot write PLC signals; inventory is never overwritten from PLC data.

## 6. Verification

Automated tests (50 for CR-010) against PostgreSQL and the simulators; see the implementation report. No real PLC, RCS or AGV was used.

## 9. Open items

| ID | Item |
|---|---|
| O-1 | Whether the AGV is released between two legs (implemented) or stays bound to the Transport Task (Q-AGV-1). |
| O-2 | M9101 ACK timing, M9120 timing, M9003/M9004/M9005 mapping to pick/drop, destination-port handshake, D1500 layout/encoding, D1514 and Rack_Type encoding, timeouts and retries, heartbeat. |
| O-3 | When a destination port transaction should open (at dispatch vs approach). |
| O-4 | Phase 2 locations (P1–P4, M5, M6) are not Phase 1 locations; reference drafts exist but cannot validate until locations are defined and mapped. |
| O-5 | Live PLC reading (`IPlcObservationSource`) and a real RCS adapter for legs. |
