# CR-035 – AGV Phase 2 first runnable increment (Two-Gate readiness, real-RCS leg template, Admin manual PLC simulation)

| | |
|---|---|
| **Status** | `IN PROGRESS – PARTIAL` (first increment implemented; Queue Request/Held/Scope and Pick/Drop triggers NOT implemented) |
| **Approval** | Project owner instruction of 2026-11-24 ("TASK: AGV Phase 2 – First Runnable Implementation" and follow-ups). The P0 Revision 1.2 specification itself remains `DRAFT`; OQ-01 closure is **not** confirmed by the owner and is not relied upon |
| **Branch** | `feature/ph2-first-runnable` (from `main` `34c45b2`, with `feature/cr-030-real-rcs-dispatch` merged **uncommitted** via `merge --no-commit`; user's uncommitted work preserved). Branch CR-030 untouched |
| **PLC mapping basis** | **Logical signals of IO Map v2 as already used by the code** (test configuration). The 20/11/2026 workbook (CR-029 `PENDING_APPROVAL`) is **not** used and no PLC address is read or written. See `docs/02_Interface_Specification/PLC_20_11_2026/PLC_MAPPING_COMPARISON_CR-035.md` |

## 1. Implemented (D-11, partially D-12)
- **Operational Readiness** axis on `business_process_versions` (`NotReady` / `Ready` / `Suspended`), independent of the publication state. New versions start `NotReady`; migration `AddProcessVersionReadiness` (columns only; **every existing row becomes `NotReady`** (Ready is never inferred from Published; hardening 2026-11-24); Down drops only those columns). Decided transitions only: NotReady→Ready, Ready→Suspended, Suspended→Ready; others refused `READINESS_TRANSITION_NOT_DECIDED` (OQ-51). Reason required; audited (`PROCESS_READINESS`). API: `GET/PUT /api/process-versions/{id}/readiness`.
- **Intake gate** behind the flag `RouteEngine:EnforceReadiness` (default **false** = behaviour unchanged): only Published + Ready versions are matched (also for an explicit process code); otherwise `PROCESS_NOT_READY`; no substitute version.
- **Real RCS leg template** (CR-030 mapping extended, config only): `Rcs:RealDispatch:LegTemplate` (taskTyp + position path roles `Source`/`Destination`, locations from `Rcs:RealDispatch:Locations`). Empty by default → legs stay Blocked exactly as in CR-030. `RealLegDispatcher` now calls `HikRcsClient.CreateLegTaskAsync` / `ContinueTaskAsync` / `QueryTaskAsync` when configured. **No RCS simulator is used.**
- **Admin manual PLC simulation** (`/api/plc-simulation/*`, Admin only, audited, UI page `/plc-simulation`): deployment switch `PlcSimulation:Allowed` (default false); refuses activation unless the **real** RCS dispatch is enabled; requests go through `IPlcRequestService` (same path as a PLC runtime), flows/locations come from declared processes (no free-text destination), safety acknowledgement required, refused when the PLC is online or the port is busy; Pick/Drop confirmation only while the **real** RCS reports the task `Executing`, through the engine's inventory rules.

## 2. NOT implemented / BLOCKED
| Item | Reason |
|---|---|
| Queue Request / Intake Record / Held / `QueueSequence` / strict FIFO per Scope | New aggregate and runtime; OQ-49, OQ-54, OQ-55, OQ-56, OQ-57, OQ-58 open. Today a request becomes an Order immediately (version pinned at order creation, not at a durable intake) |
| Q1PICK template, Pick/Drop **triggers** (Seq2/Seq6, `needTrigger=1`, `triggerTyp=1`), trigger-driven `continueTask` | XML/payload not in the repository (to be supplied by the owner). `LegTemplate` is a configurable stand-in, not Q1PICK |
| One active transaction per PLC with PLC-level FIFO (ADR-0011 D-1) | Code keeps the per-port rule |
| End-to-end run with real RCS/AMR | Needs RCS-2000 reachable, mappings, `LegTemplate`; not verifiable in this environment |
| `LOCATION_DISABLED` classification (OQ-53); validator code re-classification Publication vs Readiness | Not changed: publish gate unchanged (safe default) |

## 3. Risks / rollback
Feature flags default off; migration additive; rollback = switch flags off, `ef database update AddRcsDiagParams` (drops 4 columns). Merge of CR-030 is uncommitted on the working tree of this branch.

## 4. Hardening (2026-11-24)
- Migration default changed from `Ready` to `NotReady` (the migration was never applied outside tests). Before `RouteEngine:EnforceReadiness=true`, an Admin must set `Ready` explicitly per version (reason + audit).
- `RealLegDispatcher` asks the RCS (`queryTaskStatus` by request code) **before** every send: known task -> adopted (no duplicate), unknown task -> sent, RCS not answering -> Blocked `RCS_STATE_UNKNOWN_NOT_RESENT` (not sent). Incomplete leg locations -> Blocked. Locations/rack/destination come from the leg only. `HikRcsClient` existing methods, polling and callbacks unchanged (one method added).
- Not done (needs source data): Process Scope check before dispatch (OQ-54), one active transaction per PLC / PLC FIFO (ADR-0011 D-1, still per port), Queue, Pick/Drop triggers.
