# Change Request Register

## Status definitions

- `DRAFT`: request captured but not analyzed.
- `ANALYSIS`: impact and conflicts being assessed.
- `PENDING_APPROVAL`: proposal ready for project owner decision.
- `APPROVED`: authorized for specification/code change.
- `REJECTED`: not accepted.
- `IMPLEMENTED`: change implemented and verified.
- `CLOSED`: evidence and release documentation complete.

## Rules

- Every change to approved behavior requires a CR ID.
- A new source document is evidence/input, not automatic approval.
- Keep original source files immutable and record filename/version/date.
- One CR should represent one coherent scope.
- If a request changes multiple independent behaviors, split it into multiple CRs.
- Do not close a CR until specification, code (if applicable), tests and changelog are updated.

## Register

| CR ID | Title | Source | Status | Target version | Decision |
|---|---|---|---|---|---|
| CR-000 | Establish v1.3 change-management framework | Project owner confirmation | APPROVED | 1.3 | Framework only; no business behavior change |
| CR-001 | Authentication, Admin/Operator authorization, account management, audit trail, Operator task edit/hard-delete, Operator inventory edit | Project owner messages (2026-11-06) | **APPROVED** (2026-11-06) | Phase 1 | Final business rules confirmed by the project owner; implemented; see docs/specification/access-control.md |
| CR-002 | Replace single-PLC topology with 4 PLC connections and 16 independent Station IO sets using the common 23-signal template | Project owner confirmation and `ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx` (2026-11-06) | **IMPLEMENTED** (Simulator/Mock; real PLC untested) | Phases 1-7 | Specification addenda and controlled IO workbook updated; actual device addresses/protocol/handshake remain unconfirmed |

## New Change Request template

### CR-XXX – <title>

- **Status:** DRAFT
- **Requested by:**
- **Date:**
- **Source document:** filename, revision, date, page/slide/section
- **Current baseline:** vX.Y.Z
- **Problem / business reason:**
- **Requested behavior:**
- **Current behavior:**
- **Proposed behavior:**
- **Conflicts with existing rules:**
- **Affected specification files:**
- **Affected software components:**
- **Database impact:**
- **PLC / RCS / external integration impact:**
- **Security / safety / operational impact:**
- **Acceptance criteria:**
- **Regression tests:**
- **Rollback approach:**
- **Open questions:**
- **Decision:** PENDING
- **Approved by / date:**

## Example – do not treat as approved

### CR-EXAMPLE – Add buffer F6

- **Status:** EXAMPLE ONLY – NOT A REAL REQUEST
- **Requested behavior:** Add a new full-rack buffer location F6.
- **Potential impact:** process flows, inventory model, location configuration, UI and tests.
- **Decision:** Not approved. This example must not be implemented.

### CR-001 – Authentication, authorization, accounts, audit, Operator task and inventory rights

- **Status:** APPROVED
- **Approval date:** 2026-11-06 (project owner; the rules below are final decisions, no longer proposals)
- **Implementation:** implemented and verified (see WORK_LOG sections 11–13). Specification: docs/specification/access-control.md.
- **Requested by:** Project owner
- **Source:** project owner messages "Implementation Package – Authentication & Authorization", "Triển khai đăng nhập thực tế và quản lý tài khoản", "CR-001 – Final business rules & implementation" (2026-11-06; messages, no files)
- **Current baseline:** v1.3 (no authentication existed)
- **Not affected:** BP-01/02/03/05/06, BP-04 (REVIEW), A3/A4, D1/D2, PER_AGV policy, task state machine edges, PLC adapter, RCS adapter, rack model, "Không bind rack", initial stock at F3/F4.
- **Affected:** API (all endpoints need a session), UI, database (tables app_users, audit_log; migration AddAuthentication), tests.

#### Final rules (APPROVED)

1. Real sign-in with PostgreSQL accounts; roles Admin and Operator; passwords hashed; HttpOnly/SameSite cookie; forced password change for the bootstrap account. No simulated sign-in anywhere (Demo Mode keeps simulated DATA only).
2. **Operator may create tasks.**
3. **Operator may edit ALL request information of a task** while it is waiting, not accepted by an AGV and not sent/accepted by RCS. In this system the user-supplied request data are the process flow and the requesting station; AGV, rack kind, source rack and destination follow from the approved flow rules and are re-evaluated. Identity fields (id, request key, requester, RCS codes) never change.
4. **Operator may HARD-DELETE a task** under the same conditions. It is not converted into a cancel. The state is re-read from the database inside the transaction; a concurrent dispatch makes the delete fail (row version) and be refused.
5. The audit entry (task code, actor, time, task data, action DELETE, result) is committed in the same transaction as the delete and survives it.
6. **Operator may view and edit inventory through the existing APIs** (initial stock declaration, quantity adjustment); Admin has full rights. Backend-enforced, audited, business guards (capacity, allowed rack kind, reserved racks) unchanged. No new warehouse operations were invented; missing pieces are listed in access-control.md section 5.
7. Admin account rules: cannot delete the signed-in account (even if other Admins exist); cannot delete or disable the last active Admin; an account with history is disabled, never hard-deleted; checks are in the backend.
8. Password policy and session lifetime: NOT approved yet; the current technical defaults stay unchanged (min 10 chars with letters and digits, lockout 5 attempts / 5 minutes, 30 min idle / 12 h absolute).
9. Admin keeps managing tasks under the existing safe-state rules (waiting tasks: edit/hard-delete; running tasks: cancel / reconcile). Cancel and reconcile stay Admin-only (not delegated by the decision).

#### SUPERSEDED (do not apply)

- ~~"Edit task" = change only the requesting station~~ → rule 3.
- ~~"Delete task" = soft delete to Cancelled~~ → rules 4–5.
- ~~Operator is read-only for inventory; stock changes Admin-only~~ → rule 6.
- ~~Self-deletion refused only if last Admin~~ (earlier, looser reading) → rule 7 (always refused).

#### Still open (not decided by the owner)

HTTPS/TLS and Data Protection key backup (deployment), MFA, e-mail password reset, per-IP rate limiting, UI for stock editing, whether Operator should also get cancel/reconcile, password/session policy approval.

#### Tests

TST-AUTH-* (unit/API with in-memory fakes) and TST-AUTH-DB-* (PostgreSQL integration: task edit/hard delete/race, inventory, accounts, bootstrap).


### CR-002 – Four PLCs and 16 independent Station IO sets

- **Status:** APPROVED (architecture direction; actual PLC commissioning parameters remain open)
- **Approval date:** 2026-11-06
- **Requested by:** Project owner
- **Source:** Project owner confirmation in conversation (2026-11-06) and `ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx`.
- **Current baseline:** WCS v1.5; earlier baseline assumed one PLC connection.
- **Problem / business reason:** Four physical PLCs each manage four Stations. Each Station needs an independent IO set so PLC programming, testing and maintenance can be performed independently without using a shared request/response block across four doors.
- **Approved architecture:**
  1. Four physical PLCs and four WCS PLC connections.
  2. PLC-01 `192.168.10.101`: A1, A2, A3, A4.
  3. PLC-02 `192.168.10.111`: B1, B2, D1, D2.
  4. PLC-03 `192.168.10.121`: X1, X2, X3, X6.
  5. PLC-04 `192.168.10.131`: E1, E2, G3, G7.
  6. Each Station has an independent instance of the common 23-signal IO template; use reusable PLC/Station logic rather than four unrelated implementations.
  7. Actual addresses for Stations on the same PLC must be separately allocated. Address reuse across different physical PLCs is allowed.
  8. Signal identity is `PLC_ID + Station_ID + Signal_Name`.
- **Affected specification files:** `specification/business-rules.md` (section 1.8), `specification/integration-requirements.md` (section 5.10), `README.md`.
- **Controlled interface artifact:** `02_Interface_Specification/PLC_IO_Map/ETEK_WCS_IO_Map_4PLC_16Stations_v2_Separate_IO_Sets.xlsx`.
- **Not approved / still open:** actual per-Station M/D addresses, TCP ports, MC Protocol settings, QJ71E71-100 parameters, STRING encoding/length, PLC-wide versus Station-scoped heartbeat, handshake/reset ownership, timeout/retry values.
- **Business flows affected:** No change to BP-01/02/03/05/06 behavior. BP-04 remains REVIEW. A3/A4 and D1/D2 process behavior remains UNCHANGED; only their PLC connection/IO architecture is covered by this CR.
- **Database impact:** No schema change is authorized by this documentation CR. Implementation may propose a migration only after assessing the existing config model and obtaining approval if it changes persistent contracts.
- **Acceptance criteria:** Four configurable PLC connections; 16 Station IO instances; no address collision between Stations on the same PLC; identical device addresses allowed across different PLCs; no invented address or handshake behavior; PLC simulator and real-hardware verification reported separately.
- **Rollback:** Revert CR-002-specific config/code/document changes through Git; retain prior files/commit for traceability.
- **Decision:** APPROVED for architecture and documentation direction. Live PLC commissioning remains blocked on PLC engineer confirmation of the open parameters.
- **Implementation note (2026-11-06):** Full implementation approved and delivered (Phases 2-7). Migration `AddPlcTopology` (4 new tables, CREATE only) exists and is NOT applied to production. SOURCE CODE IMPLEMENTED: yes. SIMULATOR/MOCK TESTED: yes (302 backend tests). REAL PLC TESTED: no. Commissioning remains blocked on PLC engineer confirmation; see ADR-0004 and TESTING_REPORT_CR-002.md.
