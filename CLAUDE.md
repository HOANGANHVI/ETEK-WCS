# Claude Code – Mandatory Project Rules

Project root: ETEK WCS. Business baseline files are under `docs/` (specification, change-management and decisions). Read `README.md` and `docs/README.md` before proposing implementation. The setup script may create project scaffolding only; do not infer or implement unapproved business workflows.

You are implementing ETEK WCS. The files under `docs/specification` (or this package's `specification/` directory) are the approved business baseline.

## 1. Authority and precedence

1. Explicitly approved business rules and accepted Change Requests.
2. Current approved specification files.
3. Source documents, interpreted only within their stated scope.
4. Your technical inference, which must be labeled as a proposal and must not silently become a business rule.

If sources conflict, do not choose one silently. Record the conflict and request a decision.

## 2. Protected business rules

- Task concurrency is `PER_AGV`: at most one active task per AGV; AGV1 and AGV2 may operate concurrently.
- BP-04 (slide 8: B1/B2 has free position and requires full rack) is `REVIEW`. Do not implement until approved.
- WCS owns business rack inventory and location state.
- A3/A4 and D1/D2 are `UNCHANGED`; do not redesign or replace the legacy process.
- A single PLC connection serves the stations; do not introduce station clusters.
- Do not add bind-rack behavior at points explicitly marked “Không bind rack” in the source process.

## 3. Change-control workflow

When new requirements or documents arrive:

1. Read the source and identify exact new/changed statements.
2. Create a Change Request using `change-management/CHANGE_REQUESTS.md`.
3. Identify conflicts and affected requirements, flows, data, APIs, PLC signals, RCS integration, UI, backend and tests.
4. Produce an impact report.
5. Stop before modifying approved specification or source code until the project owner approves the Change Request.
6. After approval, update only affected files.
7. Update CHANGELOG, Change Request status, Impact Matrix and Traceability Matrix.
8. Run consistency checks and relevant regression tests.

Do not treat “please analyze this document” as permission to implement it.

## 4. Implementation safety

- Do not rewrite whole modules for a narrowly scoped change.
- Do not refactor unrelated code during a requirement change unless separately approved.
- Preserve existing APIs and database behavior unless the Change Request explicitly authorizes a breaking change.
- Use database migrations; never silently drop or rename production data.
- Use feature flags or configuration for incomplete/optional behavior where appropriate.
- Keep PLC and RCS vendor adapters isolated from business logic.
- Do not hard-code IPs, ports, credentials, PLC addresses, RCS URLs, robot codes, workstation codes or task template codes.
- Make task creation, callbacks, inventory updates and retries idempotent.
- Protect AGV, Rack and Location resources against concurrent allocation.
- Never mark a rack at its destination merely because a task was created or dispatched.

## 5. Required response before coding

For each approved change, first provide:
- Change ID and approved scope.
- Files/modules expected to change.
- Database migration impact.
- API and integration impact.
- Regression tests to run.
- Risks and rollback approach.

If implementation reveals an unapproved business ambiguity, stop and ask. Do not invent a resolution.

## 6. Completion report

After implementation, report:
- Files changed.
- Behavior added/changed.
- Tests run and results.
- Database migration details.
- Remaining risks/TBD items.
- Confirmation that protected rules and unrelated flows were not changed.


## 7. Approved PLC interface architecture update (2026-11-06)

This section records the project owner's approved direction for the Phase 1 PLC interface. It supersedes only the conflicting PLC connection and station-IO assumptions in earlier baseline text; all unrelated business rules and implementation safeguards remain unchanged.

### 7.1 Official IO Map

The current PLC IO interface workbook is:

`docs/02_Interface_Specification/PLC_IO_Map/ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx`

Use this workbook as the authoritative working IO interface reference. It contains the common 23-signal template and four PLC-specific sheets, each with four independent Station IO sets, plus PLC configuration, Station mapping, handshake-definition and address-allocation worksheets.

Do not replace it with the older 352-signal per-station workbook or the earlier 23-signal single-interface workbook.

### 7.2 PLC and Station topology

- There are 4 physical PLCs and 16 Stations in Phase 1.
- WCS maintains 4 independent PLC connections, one connection per physical PLC.
- Each PLC manages exactly 4 Stations:
  - PLC-01 (`192.168.10.101`): A1, A2, A3, A4.
  - PLC-02 (`192.168.10.111`): B1, B2, D1, D2.
  - PLC-03 (`192.168.10.121`): X1, X2, X3, X6.
  - PLC-04 (`192.168.10.131`): E1, E2, G3, G7.
- These IPs and station assignments are the currently supplied project mapping. TCP ports, MC Protocol parameters and other PLC network parameters remain `NEEDS_CONFIGURATION` until confirmed by the PLC engineer.
- Do not create 16 TCP connections. A PLC connection serves its four Stations.

### 7.3 Independent Station IO sets

- Each Station has its own independent instance of the common IO template.
- Use the same signal definitions and processing contract for all Stations; do not implement four unrelated variants.
- Each Station must have its own actual register allocation within its owning PLC, so signals for different Stations on the same PLC do not overwrite one another.
- Identical device addresses may be reused on different physical PLCs because each PLC has its own device-memory namespace.
- Identify every IO point by the composite key `PLC_ID + Station_ID + Signal_Name`.
- Do not infer or calculate register offsets. `Template_Address_Reference` is only a reference from the source IO template; `Actual_PLC_Address` must remain unassigned until confirmed by the PLC engineer.
- Any PLC-wide signal (for example, connection heartbeat) must be explicitly classified as PLC-scoped or Station-scoped by the approved interface design. Do not silently duplicate or consolidate it.

### 7.4 PLC implementation pattern

- Implement one reusable PLC communication driver per PLC connection.
- Implement one reusable Station IO handler / mapping contract, instantiated independently for each Station.
- PLC logic may use a common function block/template with four instances per PLC, provided the PLC engineer confirms the implementation platform and memory allocation.
- Keep PLC communication, Station IO mapping, handshake coordination and WCS business logic separated.
- Support independent polling, quality/status, diagnostics and handshake state per Station while preventing concurrent access races on a shared PLC connection.

### 7.5 Address, protocol and handshake controls

- Preserve signal names, directions, data types and descriptions from the official workbook.
- Never invent device addresses, data encodings, string lengths, scaling, bit/word allocation, protocol parameters, handshake transitions, reset ownership or timeout values.
- Mark missing values `NEEDS_CONFIGURATION` or `NEEDS_CONFIRMATION`.
- The use of Mitsubishi MC Protocol 3E Binary is a proposed implementation choice until the PLC engineer confirms the actual PLC Ethernet configuration.
- Validate the real PLC model, QJ71E71-100 settings, TCP port, frame type, network number, PC number, PLC station number and permitted device ranges before live connection.
- Begin live commissioning with read-only checks. Enable writes only after the PLC engineer approves the relevant signals and safe test procedure.
- Never write safety/interlock signals or motion/transfer permits merely to test connectivity.

### 7.6 Change management and document control

- Treat the workbook above as a controlled interface document.
- Any change to a signal, direction, data type, Station assignment, actual device address or handshake must follow the Change Request workflow in Section 3.
- After approval, update the workbook version, Change Request, CHANGELOG, Impact Matrix, Requirement Traceability and affected tests/source configuration.
- Preserve previous approved versions for traceability; do not silently overwrite the historical baseline.
- When the workbook is not present at the stated path, stop and report the missing source. Do not generate a substitute IO map.

### 7.7 Superseded assumption

The earlier rule in Section 2 stating that a single PLC connection serves the stations is superseded by Section 7.2: the current Phase 1 topology is four physical PLCs and four independent WCS PLC connections. The protected business rules in Section 2 otherwise remain in force, including `PER_AGV`, BP-04 `REVIEW`, WCS inventory ownership, A3/A4 and D1/D2 `UNCHANGED`, and no rack binding where the process says “Không bind rack”.
