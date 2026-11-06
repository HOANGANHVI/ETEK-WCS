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
| CR-001 | Authentication, Admin/Operator authorization, account management, audit trail, user edit/delete of waiting tasks | Project owner message "Implementation Package - Authentication & Authorization" (2026-11-06) | IMPLEMENTED (open decisions listed in the CR) | Phase 1 | Approved by the project owner's explicit instruction to implement; items marked NEEDS_CONFIRMATION below remain open |

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

### CR-001 – Authentication and authorization (Admin / Operator)

- **Status:** IMPLEMENTED (not CLOSED: open decisions below)
- **Requested by:** Project owner
- **Date:** 2026-11-06
- **Source document:** "IMPLEMENTATION PACKAGE – AUTHENTICATION & AUTHORIZATION" (message, no file)
- **Current baseline:** v1.3 (no authentication existed; every API was open)
- **Requested behavior:** sign-in, two roles (Admin, Operator), backend authorization, account management, audit trail, Operator may create/edit/delete a task only while it is waiting and not yet accepted by an AGV.
- **Business flows BP-01/02/03/05/06, BP-04, A3/A4, D1/D2, PER_AGV policy:** NOT_AFFECTED (no change). The scheduler, state machine transitions and inventory rules are unchanged.
- **Affected areas:** API (all endpoints now require a session), UI (login, role-based menus, account/audit pages, task dialogs), database (new tables app_users, audit_log; migration AddAuthentication), tests. PLC adapter, RCS adapter, task state machine: NOT_AFFECTED.
- **Decisions taken as technical proposals (NEEDS_CONFIRMATION by the project owner):**
  1. "Not yet accepted by an AGV" is implemented as task state Pending, Validating or Queued (before Dispatching). A task already sent to RCS is locked for Operator.
  2. "Edit task" changes only the requesting station (within the stations allowed for the task's flow); the flow itself cannot change. When the source follows the station, the old rack reservation is released and a rack at the new station is reserved; if none exists the edit is refused and nothing changes.
  3. "Delete task" is a soft delete: the task becomes Cancelled (history kept, reservations released). Nothing is physically removed.
  4. Admin edit/delete is limited to the same waiting states; in-flight tasks are handled by the existing Cancel / reconcile actions (Admin only). Handling of failed/cancelled tasks by Admin was not specified.
  5. Operator is read-only for inventory and locations (the package does not grant stock-change rights); stock changes, cancel, reconcile, configuration, simulator and accounts are Admin only.
  6. Password policy (min 10 characters, letters and digits, not containing the username), lockout (5 failures, 5 minutes) and session timeouts (30 minutes idle, 12 hours absolute) are conservative defaults; the customer has not specified them.
  7. One role per account; accounts are never deleted (disable only) so audit history stays meaningful.
- **Open security items:** HTTPS/TLS termination and cookie Secure flag outside Development, backup of the Data Protection key ring, password-reset by e-mail is not provided, no multi-factor authentication, no per-IP rate limiting.
- **Tests:** TST-AUTH-* (unit/API with in-memory fakes) and TST-AUTH-DB-* (PostgreSQL integration).

#### CR-001 amendment (2026-11-06, project owner message "Triển khai đăng nhập thực tế và quản lý tài khoản")

- The simulated sign-in (role buttons without password) is removed everywhere, including Demo Mode. Demo Mode still simulates DATA only; authentication, accounts and audit always use the real backend and PostgreSQL.
- Bootstrap account: admin / admin is created when no active Admin exists and no account named admin exists; forced password change at first sign-in; never recreated or reset. In Production it is NOT created unless Bootstrap:Admin:Password is configured (or AllowDefaultCredentials=true is set explicitly).
- Added: account deletion (refused for yourself, the last active Admin, and any account that already has audit history; disable instead), activation flag at creation, confirmation password in the UI, explicit message for a blocked account (shown only to someone who knows the password).
- NEEDS_CONFIRMATION: deleting is a hard delete restricted as above (the request asked to prefer disabling when traceability matters); self-deletion is refused entirely (stricter than "refused if last Admin").
