# CR-026 – Configuration Export / Import (Phase 2 MVP)

- **Status:** `IMPLEMENTED` on `feature/cr-026-config-transfer` (Phase 2). D1–D7, the CR, OQ-1…OQ-5 and the two design confirmations (1.1 `ExecuteBatchAsync`, 1.2 "block what has not started, let what started finish") were approved by the project owner on 2026-11-19. Awaiting the owner's confirmation after testing; not merged, not deployed.
- **Branch:** `feature/cr-026-config-transfer` (from `develop`). Not merged to `main`, not deployed.
- **Source:** project owner instruction (2026-11-19) after the Phase 1 survey.
- **Phases:** 2 = MVP (Location, AGV, PLC Station, Business Process); 3 = PLC Connections, PLC I/O Mapping (`Configured` only), RCS settings without secrets; 4 = history, restore from backup, UI polish and comparison report. **Only Phase 2 is planned in detail below.**

## 1. Approved decisions (baseline for this CR)

| ID | Decision |
|---|---|
| D1 | `Approved`, `WriteAllowed`, `WriteApproval` and approver data are never exported or imported. No Password, Token, Private Key or RCS secret in a package. No mechanism may restore or activate these rights from a package. |
| D2 | IO addresses may be transferred at `Configured` level only (Phase 3). After import they are unapproved; the PLC engineer re-approves on the target. No inferred address, no offset arithmetic, no auto-created mapping, no PLC write during import. |
| D3 | `appsettings` (`ProcessFlows`, `ProtectedLocationCodes`, `Scheduler`, `RouteEngine`) are out of the import scope. The manifest may carry their hash and a difference warning. The target file is never edited. |
| D4 | User accounts (incl. PasswordHash, SecurityStamp, credentials) are out of scope. |
| D5 | Apply requires maintenance mode: dispatch stopped / no new tasks, no related active Task/Order, no active Station Transaction or PLC exchange that could be affected. Otherwise Apply is refused with the reason. Running tasks are never stopped or cancelled automatically. |
| D6 | No deletion of records absent from the package. Only Create, Update, Skip, Conflict. |
| D7 | The IO Map v2 workbook is outside CR-026; it is not restored or deleted by this work. |

Additional requirements: Import-all resolves dependency order itself; selective export reports dependent groups and lets the user decide to include them; Preview shows Create / Update / Unchanged / Skip / Conflict; all writes go through the existing business services; backup and rollback are consistent; after import there is **no** PLC runtime reload, no PLC command and no Process activation unless an Admin does it separately; Safety / Interlock / Permit / Write Approval rules are untouched.

## 2. Protected rules check

| Rule | Effect of CR-026 |
|---|---|
| PER_AGV concurrency | Untouched. Import is refused while tasks are active. |
| BP-04 `REVIEW` | `ReviewBlocked` / `ReviewReason` are preserved from the target; import never clears them; Publish stays refused by `ProcessValidator`. |
| WCS owns rack inventory | Racks, reservations and inventory transactions are excluded. |
| A3/A4/D1/D2 `UNCHANGED` | Import cannot place them in a configurable route (existing validator `LOCATION_PROTECTED` still applies to any later Publish). |
| One PLC connection per PLC, no clusters | PLC connections are Phase 3 and read-only in Phase 2. |
| No bind-rack at "Không bind rack" points | `RouteCandidate.NoBind` is transferred as data, never defaulted. |
| CLAUDE.md §7.3 / §7.5 | No address is created or inferred in Phase 2 (IO points are out of Phase 2). |

## 3. Phase 2 scope

| Group | Export | Import behaviour (through existing services) |
|---|---|---|
| Location | `code`, `name`, `type`, `rackCapacity`, `allowedRackKind`, `enabled`, `requiresPlcPermission` | Create new; Update configuration fields of an existing code via `OperationalConfigService.SaveLocationAsync`. Operational fields (`ReconciliationRequired`, `LastInventoryChangeAt`, `Version`) are never read or written. |
| AGV | `code`, `role`, `rcsRobotCode`, `enabled` | **Update only** an AGV that already exists (matched by `code`), via `UpdateAgvAsync`. Unknown code → Conflict `AGV_NOT_FOUND`. `role` must equal the target's role, otherwise Conflict. No create. `DispatchHold*` and status are never written. |
| PLC Station | `stationId`, `plcId`, `order`, `locationCode`, `enabled` | **Update only** an existing station via `PlcProvisioningService.SetPortLinkAsync` (link and enabled). Unknown station or PLC → Conflict (creation belongs to Phase 3 with PLC connections). No IO point is created or touched. |
| Business Process | `code`, header, versions with `steps` and `candidates` (natural keys: stepKey, role, priority, order) | New code: create process + **Draft** version. Existing code: update the header and add a **new Draft version**; a Published/Disabled version is never edited. Nothing is validated-and-published automatically. `ReviewBlocked` stays as on the target. |

Excluded in Phase 2: PLC connections, IO points and addresses, RCS, users, racks, all operational and history tables, file configuration.

## 4. Impact report

| Area | Impact |
|---|---|
| Requirements | New requirement CFG-XFER-01 (see Traceability). No existing requirement changes. |
| Business rules | None changed (section 2). |
| Process flows | None changed. Imported Processes are Draft only. |
| Task engine / scheduler | **Needs one small addition (open question OQ-1):** a persistent Admin-set maintenance flag that blocks new task/order creation and scheduler dispatch. This is required by D5 and does not exist today (only per-AGV error hold exists). |
| Inventory | None. |
| PLC adapter / runtime | None. No reload, no read/write, no change of IO points. Station link update must not trigger a reload (to verify in implementation; the endpoint layer, not the service, currently calls reload). |
| RCS adapter | None. |
| Database | Additive migration only (see section 6). No change or drop of existing tables or columns. |
| API | New group `/api/config-transfer/*`, Admin-only. Existing endpoints unchanged. Maintenance flag adds `/api/system/maintenance` (Admin). |
| UI | New Admin page "Configuration transfer" (VI/EN, CR-025 style) under System; maintenance banner in the layout. |
| Security | Admin-only, audit of every export/import/apply, upload hardening (size, zip-slip, zip-bomb, file allow-list, checksum). Packages contain no secret by construction and pass `LogSanitizer`. |
| Tests | New unit, integration (two databases with different Guids), invariant and regression tests (section 8). |
| Documentation | CHANGELOG, IMPACT_MATRIX, TRACEABILITY updated; format documentation `docs/02_Interface_Specification/CONFIG_PACKAGE_FORMAT.md` (new, at implementation). |

**Conflicts found:** none with the approved specification. One gap: D5 needs a maintenance mechanism that the current system does not have (OQ-1).

## 5. Design (Phase 2)

- **Module:** `WCS.Application/ConfigTransfer` (group descriptors with declared dependencies, planner, executor, package reader/writer) + API/UI. One `IConfigGroup` per group (`Export`, `Plan`, `Apply`). Dependency order is derived from declared dependencies (topological sort), not hard-coded in the importer: Location → AGV → PLC Station → Business Process.
- **Package:** ZIP with `manifest.json` (`formatVersion` 1.0, `exportedAt/By`, `appVersion`, latest `schemaMigration`, per-group counts, SHA-256, `excluded[]`, `warnings[]`, `fileConfigHash` per D3), one JSON file per group, `checksums.sha256`. Natural keys only; **no Guid, no row Version, no operational field**.
- **Export:** all or selected groups; when a selected group depends on an unselected one the UI/API returns `dependencies[]` and the user chooses to include it; the manifest records `unresolvedReferences` if not included. Read in one read-only transaction so groups are consistent.
- **Import pipeline:** Upload → Validate (format, checksum, schema, duplicate natural keys, size limits) → Preview (pure dry-run, per record Create / Update / Unchanged / Skip / Conflict with field diff) → Apply (requires a Preview token bound to the package hash and to a hash of the target state; stale token → refused, Preview again).
- **Selective import:** select groups and individual records; a selected record whose dependency is neither in the target nor selected is `Conflict: MISSING_DEPENDENCY`.
- **Conflict handling:** default Skip; Update per group/record. Hard conflicts (cannot be overridden): Role change of an AGV, unknown AGV/Station, Location linked to another station, record referenced by an active Task/Order/Transaction, duplicate keys in the package, unresolved dependency.
- **Apply gate (D5):** maintenance flag ON **and** zero active Task/Order/Station Transaction/pending station request (system-wide in Phase 2, conservative – OQ-2). Otherwise HTTP 409 with the list of blockers.
- **Backup:** inside the Apply transaction, before the first write, the current state of every affected record is exported to a package in `ConfigTransfer:BackupPath` (`NEEDS_CONFIGURATION`; Apply is refused if it is not set or not writable). Backup file name carries `importId`.
- **Rollback:** the whole Apply is one database transaction; any failure rolls everything back and the target is unchanged. Restoring from a backup after commit is Phase 4. Newly created records are disabled (not deleted) on restore.
- **Idempotency:** `importId` + package hash; repeating Apply returns the first result.
- **Writes** only through `OperationalConfigService`, `PlcProvisioningService`, `ProcessAdminService` (and their audit). No direct SQL / DbContext writes. No PLC runtime call anywhere in the module.
- **Feature flag:** `ConfigTransfer:Enabled` (default `false`). The module is not registered when disabled.

## 6. Database migration impact (additive, DEV only)

| Table | Purpose |
|---|---|
| `config_transfer_sessions` | importId, package hash, status (Uploaded/Previewed/Applied/Failed/Expired), actor, timestamps, summary JSON (counts only, no content), backup file name |
| `system_maintenance` | single row: `Enabled`, `Reason`, `EnabledBy`, `EnabledAt` (OQ-1) |

No change to existing tables. Rollback of the migration = drop the two new tables (no production data in them at DEV). Never run on the deployed machine without owner approval.

## 7. API (Phase 2, Admin only)

`GET /api/config-transfer/groups` · `POST /exports` · `GET /exports/{id}` · `GET /exports/{id}/download` · `POST /imports` (upload) · `POST /imports/{id}/preview` · `POST /imports/{id}/apply` · `GET /api/system/maintenance` · `PUT /api/system/maintenance`. Existing endpoints are not changed.

## 8. Test plan

- Unit: manifest/version/checksum, zip-slip and zip-bomb limits, natural-key mapping, topological ordering, diff and classification (Create/Update/Unchanged/Skip/Conflict).
- Integration (real PostgreSQL, two databases with different Guids): export A → import B empty and B populated; references resolve by code; second Apply is idempotent.
- Safety invariants: no `Approved/WriteAllowed/WriteApproval` change, no secret in package, no IO point touched, no PLC runtime call (mock asserts zero calls), Processes stay Draft, BP-04 stays blocked, A3/A4/D1/D2 untouched, Role of AGV unchanged.
- Gate: Apply refused without maintenance, with an active Task/Order/Transaction, without backup path; Operator gets 403.
- Failure: forced error at record N → full rollback, nothing changed.
- Regression: existing Config, Process, PLC, Concurrency suites; frontend tsc, ESLint, i18n check, build, 6 smoke suites; real-browser check of the new page (Admin/Operator, VI/EN, 3 viewports).

## 9. Risks and rollback of the CR

| Risk | Mitigation |
|---|---|
| Config change while the system is dispatching | Maintenance flag + zero-active gate (D5) |
| Maintenance flag blocks operations if forgotten | Visible banner, Admin-only, audited; flag is only set by Admin |
| Process imported against unapproved IO | Draft only; Publish validator unchanged |
| Malicious package | Upload hardening, checksum, allow-listed names, schema validation |
| Partial apply | Single transaction + backup |
| Scope creep into PLC/IO | Phase 2 contains no PLC data; reload code never referenced (test) |

Rollback of the CR: set `ConfigTransfer:Enabled=false` (no behaviour change); revert the branch; drop the two additive tables.

## 10. Open questions requiring owner decision before coding

1. **OQ-1 Maintenance mechanism.** D5 requires "stop dispatch / prevent new tasks", which does not exist as a global switch. Proposal: a persistent Admin-set maintenance flag that makes task/order creation return a clear refusal and makes the scheduler skip new dispatch (running tasks continue untouched). Touches Task creation and Scheduler entry points (small, guarded by the flag). Approve, or choose another mechanism?
2. **OQ-2 Active-work gate scope.** Phase 2 proposal: system-wide "no active Task/Order/Station Transaction" (simple and safe). Alternative: only entities related to the imported records.
3. **OQ-3 Backup location.** `ConfigTransfer:BackupPath` has no default and must be set per deployment; Apply is refused if unset. Confirm.
4. **OQ-4 Stations.** Phase 2 is update-only for stations (link and enabled); creating stations waits for Phase 3. Confirm.
5. **OQ-5 Process update.** Importing an existing Process code adds a new Draft version and updates the header; confirm that the header update (name/description/trigger locations) is allowed, or choose "new Draft version only".

## 12. Implementation notes (what was decided while building; all inside the approved scope)

- `IUnitOfWork.ExecuteBatchAsync` (and `ExecuteSnapshotAsync`, REPEATABLE READ, for a consistent Export): one transaction that the existing services join. Verified with PostgreSQL for the Location, AGV, Process and PLC-port services and `IAuditLog.Enlist`. Realtime events raised inside a batch are held until the commit (`BatchEffects` / `BatchAwareNotifier`).
- Maintenance gate: `SELECT … FOR SHARE` in every unit that starts new work, `FOR UPDATE` when Maintenance is switched; two deterministic tests fail when the lock is removed. Entry points held or refused: `CreateTask`, station-request intake (stays Pending, no attempt, no acknowledgement), `RouteEngine.StartAsync`, PLC request recording and orphan resume, `ProcessTask`, `TryDispatchNext`, `DispatchToRcs`, first-leg creation and dispatch. Later legs of a running order, signals, cancel and reconciliation are not held.
- Export is a synchronous download (the data set is small) instead of a background job. The preview selection is stored with the session; Apply uses the previewed selection and token.
- A **new** Business Process whose package record is REVIEW-blocked is a Conflict (`PROCESS_REVIEW_BLOCKED_NOT_TRANSFERRABLE`): an import neither creates nor changes the REVIEW block. The type of an existing Location cannot be changed by the existing update path and is reported as a Conflict.
- Known limits: the stochastic Maintenance race test is supplementary (the deterministic tests prove the lock); package warnings coming from the server are shown in English; the readiness list is taken at Preview time (Apply re-checks everything).
- PLC risk: while Maintenance is ON, PLC station requests wait as Pending and are not acknowledged, so a PLC handshake that has a timeout may time out on the PLC side. The PLC protocol was not changed; schedule maintenance with this in mind.

## 11. Implementation order (after approval)

1. Migration + maintenance flag (OQ-1) with tests. 2. Package model, manifest, reader/writer, hardening. 3. Group descriptors (Location, AGV, Station, Process) with Export. 4. Planner / Preview. 5. Executor (gate, backup, transaction, idempotency). 6. API. 7. UI (VI/EN, CR-025 look). 8. Tests, docs (CHANGELOG, Impact Matrix, Traceability, package format). Each step is committed separately on the feature branch.
