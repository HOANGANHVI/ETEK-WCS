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

> **Status update – CR-029, decisions D-1 to D-4 of 2026-11-21 (ADR-0011).** Sections 7.1–7.7 are kept **as the historical record**. The PLC interface workbook of 20/11/2026 is the **new interface document, pending PLC engineer confirmation and owner approval**; the IO Map v2 workbook named in 7.1 is the **old baseline** (reference and historical comparison only, not the default basis for new design). Section **7.8** governs new PLC interface design work and prevails over 7.1, 7.3 and 7.4 where they conflict.

This section records the project owner's approved direction for the Phase 1 PLC interface. It supersedes only the conflicting PLC connection and station-IO assumptions in earlier baseline text; all unrelated business rules and implementation safeguards remain unchanged.

### 7.1 Official IO Map

> **Superseded as a design basis by Section 7.8 (CR-029 / ADR-0011, 2026-11-21): this workbook is the OLD baseline – historical reference only.**

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


### 7.8 PLC interface of 20/11/2026 – status and decisions D-1 to D-4 (CR-029, ADR-0011, 2026-11-21)

Source documents and specification set: `docs/02_Interface_Specification/PLC_IO_Map/AGV_PLC Interface_List_20_11_2026.xlsx` and `docs/02_Interface_Specification/PLC_20_11_2026/`. Change Request: `docs/change-management/CR-029_PLC_INTERFACE_20_11_2026.md` (status `PENDING_APPROVAL`: pending PLC confirmation; **not** approved and **not** ready for implementation). Decision record: `docs/decisions/ADR/ADR-0011-plc-interface-20-11-2026-architectural-decisions.md`.

**Status of the documents**
- The 20/11/2026 workbook is the **new interface document, pending confirmation/approval**. It is **not an approved mapping**: do not implement it against, write to, or deploy it to a real PLC. Its register-address column is empty; do not invent addresses, data encodings, reset ownership, timeouts or handshake transitions (see 7.5, which still applies).
- The IO Map v2 workbook (7.1) and its 23-signal template are the **old baseline**, kept for reference and historical comparison. Do not use them as the default basis for new design. Do not delete them or the older PLC documents (they are marked SUPERSEDED / partly obsolete, not removed).
- Station naming of the fourth PLC (E1, E2, G3, G7 in 7.2 versus E1, E2, G1, G2 in the 20/11/2026 workbook) is **still open**; do not change the configured stations until the PLC engineer answers (PQ-20).

**D-1 – PLC transaction concurrency (target architecture, not implemented).** At most **one active Handshake transaction per PLC**, with **one FIFO queue per PLC**. The four PLCs run independently and in parallel; Stations of one PLC never perform a Handshake at the same time. This supersedes in part ADR-0008 decision 4 ("one active Station Transaction per port"). The current code has **no PLC-level FIFO** (a busy port is rejected, not queued) and the existing tests describe the per-port model: never state that PLC-level FIFO is supported until code and tests exist. The start and end of the "active Handshake" window are not defined by any source and must not be assumed.

**D-2 – Mission_Feedback and the dynamic AMR fleet.** AGV1/AGV2/AGV3 are not a fixed identity of an AMR in the WCS–RCS architecture. The RCS manages the dynamic fleet and chooses the AMR; WCS stores the real `RobotCode` supplied by the RCS. `Mission_Feedback` reflects the receipt/processing status of the request and does **not** represent a `RobotCode` by default. Do not edit the source workbook's enum and do not write 1–3 as an AMR identity. A new feedback interface, and any data area carrying the real `RobotCode`, must be agreed with the PLC side.

**D-3 – This update.** The 20/11/2026 interface is recorded as new and pending; IO Map v2 is the old baseline; nothing historical was removed.

**D-4 – Physical rack reconciliation.** The loss of physical-rack-versus-inventory reconciliation is **not accepted**. The following must be established: rack presence at the Station; rack identity when the process requires it; a unique transaction identity / duplicate protection; the safety/interlock conditions; and, if the PLC cannot provide rack identification, another device or system that does. `PLC_MissionCompleted` reports **`1` = Failure, `2` = Success** (corrected 2026-11-26, OW-4; the earlier wording named the value `1`, from the older workbook revision). `PLC_MissionCompleted = 2` is necessary but **not sufficient** evidence that the correct rack was handed over at the correct location, and no single INT value is read as completion before it is verified to belong to the current transaction; WCS confirms a hand-over only when presence (and identity when required) is confirmed, the rack is at the right Location, the rack status and position are updated successfully in WCS and the history records it. WCS keeps owning inventory.

**Implementation gate.** No code, migration, database or configuration change for the 20/11/2026 interface until CR-029 is approved, the PLC engineer has answered the clarification questions that block the change, and a migration plan has been approved (Section 3 workflow).

### 7.9 Single IO map (owner decision of 2026-11-26, CR-045)

- The **only** PLC IO map of the project is `docs/02_Interface_Specification/PLC_IO_Map/AGV_PLC Interface_List_22_11_2026.xlsx` (SHA-256 `405586c9e4d2893f1d3a33922259ecfe1c400ca877710877b3b482e049b34da3`). It prevails over every workbook named in 7.1–7.8, in this file and elsewhere. Those workbooks are retired and removed from the repository (Git history keeps them); do not restore them into the IO map folder. Sections 7.1–7.8 remain as the record of how the interface evolved.
- It is **not yet an IO contract confirmed by the PLC engineer**: do not implement against it, write to or deploy it to a real PLC until the confirmations and the change approval exist (7.5 and the implementation gate of 7.8 still apply). Keep the status of every signal in `PLC_IO_Map/README.md`: agreed by the owner / not confirmed / waiting for a real test.
- Changing the source document does not change the running PLC catalog, database or communication logic.
- Real PLC writes are denied by default (CR-044): a write needs the deployment declaration and may not reach a reserved register of the unconfirmed interface.
