# ADR-0002: Phase 1 Core Platform – Technical Decisions

- **Status:** Proposed (technical; awaiting project-owner review)
- **Date:** 2026-11-05
- **Scope:** Technical design choices made while implementing Phase 1. **None of them changes an approved business rule.**
  Items marked `NEEDS_CONFIRMATION` are proposals that must not be treated as business decisions.

## Context

Phase 1 implements the domain model, persistence, task orchestration, inventory, scheduler, simulators, API and a frontend
foundation on top of WCS Specification v1.3. The specification lists the task states but not the transitions, does not fix the
rack data model and does not provide location capacities, PLC addresses or RCS mappings.

## Decisions

### D1. Task transition table (derived, technical)
States and the AGV-occupying subset are exactly those of `task-state-machine.md` 3.2/3.3. The edges are a derivation implemented in
`TaskStateMachine`; every other transition throws. Notable choices:
- `PENDING/VALIDATING/QUEUED -> CANCELLED` directly: nothing was sent to RCS, so there is nothing to confirm.
- Any dispatched task needs `CANCEL_PENDING` first and becomes `CANCELLED` only after RCS confirmation (callback or status query),
  per 3.9.
- `EXECUTING -> COMPLETED` only after a confirmed drop. An RCS "end" without a confirmed drop never completes a task.

### D2. PER_AGV is enforced by the database
A partial unique index `ux_transport_tasks_one_active_per_agv` on `transport_tasks("AgvId")` restricted to the seven occupying states.
Application code additionally checks first, but the index is the guarantee. Conflicts re-run the unit of work (xmin optimistic
concurrency + unique violations).

### D3. Resource locking
- Rack: unique index on active source-rack reservations (`reservations."RackId"`).
- Location capacity: `occupied racks + active destination reservations < capacity`, with the location row touched on every
  reservation so concurrent allocations conflict on its `xmin` token.
- Reservations are **kept** after a failure/cancel that touched the physical world, until an operator reconciles
  (`inventory-management.md` 4.4, 4.8). Tasks that never reached RCS release immediately.

### D4. Rack model – `NEEDS_CONFIRMATION`
Unresolved in the specification (individual Rack ID vs. quantity per type). Proposal implemented so that **no logic depends on the
answer**: every rack has a WCS surrogate id and an *optional* `ExternalRackId`. Quantity-only operation = racks without an external
id (initial stock declaration creates anonymous rack rows). If the owner chooses a pure quantity model, only `Rack`/`RackRepository`
and `InventoryService` change; task, scheduler and API contracts do not.

### D5. Flow rules are configuration
Source/destination candidate groups, allowed request stations and "no bind" drop locations live in `ProcessFlows` configuration.
The order of groups transcribes the order shown in `process-flows.md` and is **not** a confirmed priority policy
(`NEEDS_CONFIRMATION`). Inside a group the configured order is used as a deterministic tie-break only. Queue order is FIFO by creation
time as a neutral default (`NEEDS_CONFIRMATION`). A `Bp04` entry makes startup fail.

### D6. "No bind" semantics
A drop confirmed at a location listed in `NoBindDropLocations` for that flow records the rack at the location but never binds an
external rack id (outcome `NotBoundByProcess` in the inventory transaction). Elsewhere a supplied id is bound. The meaning of "bind"
in the legacy process is otherwise unspecified (`NEEDS_CONFIRMATION`).

### D7. `continueTask` timing – `NEEDS_CONFIRMATION`
`process-flows.md` BP-01 step 8 calls `continueTask` right after the pick, then checks destinations (step 9); `task-state-machine.md`
3.6 says to continue *after* a destination is reserved. Implemented: **continue after the destination is reserved** (3.6), so the
AGV is never released toward an unknown destination. Needs confirmation together with the RCS task-template behaviour.

### D8. Vendor isolation
`IPlcAdapter`, `IRcsClient`, `IStationSignalService` live in `WCS.Application`; only in-memory simulators exist
(`Integration:Mode=Simulator`). Selecting any other mode fails at startup. No PLC address, RCS URL, robot code or template code is in
source; robot codes are data on the `agvs` row. The simulator API (`/api/simulator/*`) injects vendor-neutral signals and is **not** an
RCS callback API; no RCS callback endpoint is created until the real contract is confirmed.

### D9. Timeouts are configuration, off by default
`Scheduler:StateTimeoutsSeconds` (per state). Real values require agreement with the PLC/RCS teams. `WAITING_FOR_DESTINATION` has no
timeout unless configured explicitly.

### D10. Secrets
The database password is read from User Secrets (`Database:Password`) or `WCS_DB_PASSWORD` / `Database__Password`; the committed
connection string has no password.

### D11. Dependency patch levels
EF Core Design 10.0.14, EF Core Relational 10.0.14 and Npgsql EF provider 10.0.3 replace the scaffold's 10.0.0 / 10.0.0. This removes
the transitive `System.Security.Cryptography.Xml/Pkcs 9.0.0` advisories (NU1902/NU1903) without suppressing NuGet audit.

## Consequences
- Business behaviour is unchanged; open questions are visible in code (`NEEDS_CONFIRMATION`) and here.
- A future Change Request is required if the owner confirms a different rack model, priority policy or `continueTask` timing.
