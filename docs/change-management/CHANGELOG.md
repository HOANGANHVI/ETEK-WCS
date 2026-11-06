# Change Log

## v1.4 – Authentication and authorization (CR-001)

- Change Request: CR-001 (IMPLEMENTED; see the register for open decisions).
- Added cookie-based sign-in, Admin/Operator roles enforced in the backend, account management, audit trail.
- Operators may create tasks and edit/delete tasks that are still waiting (not yet accepted by an AGV).
- New tables app_users and audit_log (migration AddAuthentication, not applied automatically).
- No change to BP-01/02/03/05/06, BP-04 (still REVIEW), A3/A4, D1/D2, PER_AGV or the task state machine.

## v1.3 – Change-management framework

**Status:** Framework release; no approved business behavior change.

- Added controlled Change Request process.
- Added impact matrix and requirement traceability.
- Added Claude Code guardrails.
- Preserved the v1.2 business baseline without changing its approved decisions.

## v1.2 – Initial business baseline

- Task concurrency: `PER_AGV`.
- BP-04 / slide 8: `REVIEW`.
- Rack inventory ownership: `WCS`.
- A3/A4 and D1/D2: `UNCHANGED`.
- One PLC connection; no station clustering.
- Process flows BP-01 to BP-06 documented with unresolved source ambiguities retained.

## Change log entry template

### vX.Y.Z – <short title>

- Change Request:
- Status:
- Source:
- Added:
- Changed:
- Deprecated:
- Specification files changed:
- Software components affected:
- Tests added/updated:
- Approved by:
- Date:
