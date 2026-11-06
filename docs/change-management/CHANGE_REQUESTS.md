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
