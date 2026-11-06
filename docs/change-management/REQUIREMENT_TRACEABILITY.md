# Requirement Traceability Matrix

Use stable IDs so requirements can be traced from source to specification, code and tests. Add IDs rather than relying only on headings or file names.

| Requirement ID | Requirement | Source / evidence | Specification location | Software area | Test reference | Status |
|---|---|---|---|---|---|---|
| BR-01 | One active task per AGV; AGV1 and AGV2 may run concurrently | User-confirmed decision | business-rules.md; task-state-machine.md | Task scheduler | TST-CONC-001 | APPROVED |
| BR-02 | BP-04 slide 8 is not approved for implementation | User-confirmed decision; Process in detail slide 8 | business-rules.md; process-flows.md | None until approved | N/A | REVIEW |
| BR-03 | WCS owns rack inventory/business location state | User-confirmed decision | business-rules.md; inventory-management.md | Inventory service | TST-INV-* | APPROVED |
| BR-04 | A3/A4 and D1/D2 preserve old process | User-confirmed decision; source slide 3 | business-rules.md; process-flows.md | Legacy flow | TST-LEGACY-* | UNCHANGED |
| BR-05 | One PLC connection; no station clustering | User-confirmed project constraint | business-rules.md; integration-requirements.md | PLC adapter/config | TST-PLC-001 | APPROVED |
| BP-01 | A1/A2 full rack pickup and destination selection | Process in detail, slide 5 | process-flows.md | Orchestrator, inventory, PLC/RCS adapters | TST-BP01-* | BASELINED |
| BP-02 | A1/A2 empty rack replenishment | Process in detail, slide 6 | process-flows.md | Orchestrator, inventory, PLC/RCS adapters | TST-BP02-* | BASELINED |
| BP-03 | B1/B2 empty rack pickup and delivery | Process in detail, slide 7 | process-flows.md | Orchestrator, inventory, PLC/RCS adapters | TST-BP03-* | BASELINED |
| BP-05 | F5 full rack request | Process in detail, slide 9 | process-flows.md | Operator request, orchestrator, inventory | TST-BP05-* | BASELINED |
| BP-06 | F5 empty rack return | Process in detail, slide 10 | process-flows.md | Operator request, orchestrator, inventory | TST-BP06-* | BASELINED |

## Traceability rules

- Every approved business requirement must have a stable ID.
- Every implemented requirement must link to at least one automated or documented acceptance test.
- Every test must identify the requirement(s) it verifies.
- Source references must include filename and page/slide/section where possible.
- When a requirement changes, preserve its ID if its meaning remains continuous; create a new ID if it represents a distinct requirement.
- Mark removed requirements `DEPRECATED`; never silently delete their history.
- Do not mark REVIEW/TBD requirements as implementation-ready.
| SEC-01 | Every API requires authentication; unauthenticated calls return 401 | CR-001 | CR-001 | WCS.Api (cookie auth, fallback policy) | TST-AUTH-001 | IMPLEMENTED |
| SEC-02 | Admin-only: PLC/RCS and operational configuration, simulator, accounts, audit, stock changes, cancel/reconcile | CR-001 | CR-001 | WCS.Api policies | TST-AUTH-002 | IMPLEMENTED |
| SEC-03 | Operator may create tasks and edit/delete only waiting tasks; state re-checked from the database in a transaction | CR-001 | CR-001 | TaskOrchestrator.UpdateTaskAsync / DeleteTaskAsync | TST-AUTH-DB-001..005 | IMPLEMENTED (interpretation NEEDS_CONFIRMATION) |
| SEC-04 | Accounts: no self-registration, no self role change, last active Admin protected, passwords stored hashed | CR-001 | CR-001 | UserAdminService, PasswordService | TST-AUTH-003..008 | IMPLEMENTED |
| SEC-05 | Audit trail of sign-in, task, configuration, account and simulator actions without secrets | CR-001 | CR-001 | IAuditLog, audit_log | TST-AUTH-009, TST-AUTH-DB-006 | IMPLEMENTED |
