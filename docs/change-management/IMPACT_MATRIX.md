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
| PLC topology and Station IO (CR-002) | AFFECTED (connection topology only) | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED | AFFECTED (4 PLC connections, 16 Station IO sets) | NOT_AFFECTED | AFFECTED (PLC monitoring/config UI to assess) | UNKNOWN (assess current config schema before coding) | AFFECTED (PLC mapping, isolation, reconnect, simulator/hardware tests) |\n| Authentication / authorization (CR-001) | Roles Admin/Operator (new) | NOT_AFFECTED | NOT_AFFECTED (edit/delete of waiting tasks added at API level) | NOT_AFFECTED | NOT_AFFECTED | NOT_AFFECTED | AFFECTED (login, role menus, accounts, audit) | AFFECTED (app_users, audit_log) | AFFECTED (TST-AUTH-*) |
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
