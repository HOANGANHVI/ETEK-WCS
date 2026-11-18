# Impact Matrix

This matrix is a starting point. Update it for every approved Change Request. “Potential” means assess before changing; it does not authorize a change.

| Requirement / flow | Business rules | Process flows | Task engine | Inventory | PLC adapter | RCS adapter | UI/PDA | Database | Tests |
|---|---|---|---|---|---|---|---|---|---|
| PER_AGV concurrency | Core | All task flows | Core | Resource locks | None expected | Task assignment/status | Task monitoring | Task/AGV constraints | Concurrency tests |
| BP-01 A1/A2 full rack | Related | Core | Core | Core | Station handshake | AGV1 task | Monitoring | Task/inventory | BP-01 tests |
| BP-02 A1/A2 empty rack | Related | Core | Core | Core | Station handshake | AGV2 task | Monitoring | Task/inventory | BP-02 tests |
| BP-03 B1/B2 empty rack | Related | Core | Core | Core | Station handshake | AGV2 task | Monitoring | Task/inventory | BP-03 tests |
| BP-04 slide 8 | REVIEW | REVIEW | REVIEW | REVIEW | REVIEW | REVIEW | REVIEW | REVIEW | No implementation test until approved |
| BP-05 F5 full rack | Related | Core | Core | Core | Beeper input if PLC-connected | AGV1 task | Operator/PDA | Task/inventory | BP-05 tests |
| BP-06 F5 empty rack | Related | Core | Core | Core | Beeper input if PLC-connected | AGV2 task | Operator/PDA | Task/inventory | BP-06 tests |
| A3/A4, D1/D2 | UNCHANGED | Legacy reference | Do not alter | Do not alter absent approved CR | Do not alter absent approved CR | Do not alter absent approved CR | Do not alter absent approved CR | Do not alter absent approved CR | Legacy regression |
| PLC topology and Station IO (CR-002) | AFFECTED (connection topology only) | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED | AFFECTED (4 PLC connections, 16 Station IO sets) | NOT_AFFECTED | AFFECTED (PLC monitoring/config UI to assess) | AFFECTED (4 new tables, migration AddPlcTopology) | AFFECTED (PLC mapping, isolation, reconnect, simulator/hardware tests) |\n| Authentication / authorization (CR-001) | Roles Admin/Operator (new) | NOT_AFFECTED | NOT_AFFECTED (edit/delete of waiting tasks added at API level) | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED | AFFECTED (login, role menus, accounts, audit) | AFFECTED (app_users, audit_log) | AFFECTED (TST-AUTH-*) |
| MC Protocol driver integration (CR-003) | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED | AFFECTED (driver, timeouts, address bases; PLC adapter stays isolated) | NOT_AFFECTED | AFFECTED (PLC config tab, exchange diagnostics) | AFFECTED (5 nullable columns, migration AddMcAddressBasesAndTimeouts) | AFFECTED (TST-PLC-MC-*: parity, protocol, address, ambiguous write, approved-only polling) |
| New buffer location | Assess | Likely | Assess | Likely | Assess | Assess | Likely | Likely | New + regression |

## Per-CR impact checklist

For every CR, explicitly mark each area `AFFECTED`, `NOT_AFFECTED`, or `UNKNOWN`:
- Business rules
- Process flows
- Task scheduler/state machine
- Inventory/rack/location model
- PLC signals and handshake
- RCS API/task templates/callbacks
- Backend services and APIs
- Frontend screens and PDA
- Database schema/data migration
- Logging/monitoring/security
- Automated tests and regression tests
- Deployment/configuration/rollback
| PLC configuration reorganization (CR-004) | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED (driver untouched) | NOT_AFFECTED | AFFECTED (API: pollable field, X/Y rejected) | AFFECTED (menu, 2 new screens, monitoring read-only) | NOT_AFFECTED (no migration) | NOT_AFFECTED | AFFECTED (+8 tests) | NOT_AFFECTED |
| Dockerization and Windows deployment (CR-006) | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED (driver untouched) | NOT_AFFECTED | AFFECTED (/healthz, cookie flag, Production guard) | AFFECTED (notice only) | NOT_AFFECTED (no migration; migrator runs existing ones) | NOT_AFFECTED | AFFECTED (+11 tests; Docker tests pending) | NOT_AFFECTED |
| Centralized logging and diagnostics (CR-007) | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED (PlcWorker/MC driver untouched; events mirrored in PlcEventDispatcher) | NOT_AFFECTED | AFFECTED (+3 Admin GET endpoints) | AFFECTED (new Admin screen) | AFFECTED (additive migration AddSystemLog) | NOT_AFFECTED | AFFECTED (+28 backend tests, +20 script checks) | NOT_AFFECTED |
| Dispatch and scheduler reliability (CR-008A) | NOT_AFFECTED | NOT_AFFECTED | AFFECTED (retry spacing and cancel reconciliation; state edges unchanged) | NOT_AFFECTED (rules unchanged; reservations are only kept, never released earlier) | NOT_AFFECTED (PlcWorker/MC driver untouched) | AFFECTED (Production refuses simulator dispatch; RCS contract unchanged except the additive guard) | AFFECTED (health.integration.rcs; 422 RCS_NOT_CONNECTED) | AFFECTED (additive migration: 2 columns) | AFFECTED (25 new tests; 463/463 total) |
| PLC station intake reliability (CR-008B-2) | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED (task creation unchanged and idempotent) | NOT_AFFECTED | NOT_AFFECTED (no handshake/address/MC change; Request_Ack still not written) | NOT_AFFECTED | NOT_AFFECTED | AFFECTED (additive migration: station_requests) | AFFECTED (12 new tests; 474/474) |
| CancelPending reconciliation (CR-008B-3) | NOT_AFFECTED (rules unchanged; spec 3.9 now followed literally) | NOT_AFFECTED | AFFECTED (CancelPending retry-limit outcome = Failed; NotFound no longer Cancelled; state edges unchanged) | NOT_AFFECTED (reservations and rack kept; reconcile logic unchanged) | NOT_AFFECTED | AFFECTED (cancel follow-up only; contract unchanged) | AFFECTED (+3 Admin endpoints; Admin reconciliation panel) | NOT_AFFECTED (no migration) | AFFECTED (7 new tests, CR-008A tests adapted) |
| Rack and inventory uncertainty (CR-008B-4) | NOT_AFFECTED (spec 4.8 followed more fully) | NOT_AFFECTED | AFFECTED (FinishAsync marks uncertainty for RCS Completed/Interrupted/NotFound-after-accept) | AFFECTED (InventoryService: +MarkPhysicalUnprovenAsync; allocation rules unchanged, flagged locations/Unknown racks were already skipped) | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED (no migration) | AFFECTED (9 new tests; 491/491) |
| AGV dispatch hold (CR-008B-5) | NOT_AFFECTED | NOT_AFFECTED | AFFECTED (orchestrator honours the hold; FinishAsync/RcsEnded set it) | AFFECTED (AgvHoldService, AGV DTO, release endpoint; InventoryService drop tolerance) | NOT_AFFECTED | NOT_AFFECTED (no RCS command added) | AFFECTED (AGV card, release form) | AFFECTED (additive migration AddAgvDispatchHold) | AFFECTED (15 new tests; 506/506) |
| Enable PLC write: no environment block, RealReadWrite default, write keys in the deployment (CR-015) | AFFECTED (write path available in Production; WritePolicy, approvals and safety signals unchanged) |
| PLC write approval per point and typed write input (CR-014) | AFFECTED (write permission model; WritePolicy, modes and handshake unchanged) |
| PLC diagnostic logging and export (CR-013) | AFFECTED (logging/instrumentation and a read-only Admin export; PLC behaviour unchanged) |
| IO polling without approval (CR-012; supersedes the CR-003 read rule) | AFFECTED (read path only; write, handshake and connection guards unchanged) |
| Bulk READ approval of IO points of the commissioning PLC (CR-011) | AFFECTED (PLC IO approval only; read permission, no write) |
| Configurable Business Process, Route Engine, multi-leg Transport Task, per-port Station Transaction (CR-010) | AFFECTED (addenda 1.9 / 2.4; rules unchanged) | AFFECTED (flows now also expressible as configuration; BP-04 still REVIEW; A3/A4 and D1/D2 protected) | NOT_AFFECTED (TaskStateMachine untouched; new Order/Leg/Transaction state families are separate) | NOT_AFFECTED (InventoryService untouched; legs use the same tables and events) | AFFECTED (no PLC write; request validation, transaction per port, recovery; live reading open) | NOT_AFFECTED (RCS client untouched; simulator-only leg dispatcher) | AFFECTED (new /api/routing, /api/processes, PLC provisioning endpoints) | AFFECTED (Routing, Processes, PLC provisioning screens) | AFFECTED (additive migration AddRoutingEngine: 8 tables) | AFFECTED (47 tests) | NOT_AFFECTED |
