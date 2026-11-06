# ADR-0003 – Access control, task edit and hard delete

**Status:** Accepted (CR-001 approved 2026-11-06). **Supersedes:** the proposal parts of the first CR-001 implementation (station-only edit, soft delete, Operator read-only inventory).

## Decisions
1. **Session**: cookie authentication, user store in PostgreSQL (`app_users`), ASP.NET Core Identity `PasswordHasher`. Every request re-validates the account (disabled / role / security stamp), so changes take effect immediately.
2. **Authorization** only in the backend: fallback policy "authenticated", `Operations` (Admin or Operator) for the API and hub, `AdminOnly` for configuration, simulator, accounts, audit, task cancel/reconcile. Inventory stock operations moved from AdminOnly to Operations (CR-001).
3. **Hard delete of tasks**: one transaction loads the task, re-checks "untouched by AGV/RCS", stages the audit entry in the same unit of work (`IAuditLog.Enlist`), removes task + history + reservations + processed events. The audit table has no foreign key to tasks. A concurrent scheduler update changes the row version (xmin); the unit of work retries, re-reads and refuses. A foreign-key violation raised by a concurrent history insert is treated as a retryable conflict.
4. **Edit of tasks** replaces flow and requesting station; AGV, rack kind and source are recomputed with the same rules as creation (no manual override, to stay inside approved flow rules). No state-machine edge was added: the task keeps its waiting state.
5. **Accounts with history are never hard-deleted** (audit entries, requested tasks, inventory changes); the history check lives behind `IAuditLog.CountByActorAsync`.
6. Password policy and session lifetime remain technical defaults until the project owner approves them.

## Consequences
- A deleted task cannot be recovered; the audit entry is the only trace.
- Operator can change inventory counts through the API; the web UI for it does not exist yet.
- Migrations: none (tables from AddAuthentication are sufficient).
