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
| SEC-03 | Operator may create tasks, edit ALL request data and HARD-delete only waiting tasks untouched by AGV/RCS; state re-checked from the database in a transaction; audit survives the delete | CR-001 (approved 2026-11-06) | docs/specification/access-control.md 3-4 | TaskOrchestrator.UpdateTaskAsync / DeleteTaskAsync, IAuditLog.Enlist | TST-AUTH-DB-001..008 | APPROVED / IMPLEMENTED |
| SEC-04 | Accounts: no self-registration, no self role change, last active Admin protected, passwords stored hashed | CR-001 | CR-001 | UserAdminService, PasswordService | TST-AUTH-003..008 | IMPLEMENTED |
| SEC-05 | Audit trail of sign-in, task, configuration, account and simulator actions without secrets | CR-001 | CR-001 | IAuditLog, audit_log | TST-AUTH-009, TST-AUTH-DB-006 | IMPLEMENTED |
| SEC-06 | Operator may view and edit inventory through the existing stock APIs; changes audited; business guards unchanged | CR-001 (approved 2026-11-06) | docs/specification/access-control.md 5 | Endpoints (inventory), InventoryService | TST-AUTH-DB-009 | APPROVED / IMPLEMENTED |

| PLC-01 | Four physical PLCs, one WCS connection per PLC | CR-002; owner-confirmed PLC mapping (2026-11-06) | business-rules.md 1.8; integration-requirements.md 5.10 | PLC Manager / PLC adapters | TST-PLC-CONN-* (McProtocolTests, McEndpointTests, PlcWorkerTests) | APPROVED / IMPLEMENTED (simulator+mock); real PLC untested |
| PLC-02 | Each of 16 Stations has an independent instance of the common 23-signal IO template | CR-002; official IO workbook | integration-requirements.md 5.10; official IO workbook | Station IO mapping / handler | TST-PLC-IO-* (PlcWorkerTests, PlcRuntimeIntegrationTests) | APPROVED / IMPLEMENTED (simulator+mock); addresses pending |
| PLC-03 | Signal identity is PLC_ID + Station_ID + Signal_Name; addresses may repeat across physical PLCs but not collide across Stations on the same PLC | CR-002 | integration-requirements.md 5.10; official IO workbook | IO configuration validation | TST-PLC-ADDR-* (PlcConfigTests, PlcConfigIntegrationTests) | APPROVED / IMPLEMENTED (validator); allocation pending |
| PLC-04 | Modes SIMULATOR/READ_ONLY/READ_WRITE, no auto-switch, Safety/Transfer Permit never test-written | CR-002; CLAUDE.md 7.5 | ADR-0004 | WritePolicy, PlcRuntime | TST-PLC-MODE-* (PlcConfigTests, PlcRuntimeIntegrationTests) | IMPLEMENTED (simulator+mock) |
| PLC-05 | Handshake: no unapproved transitions; WCS integration opt-in and idempotent | CR-002 | ADR-0004 | HandshakeCoordinator, PlcStationSignalService | TST-PLC-HS-* (HandshakeTests, PlcWcsIntegrationTests) | IMPLEMENTED (no rules approved) |
| PLC-06 | Operator monitors only; sensitive PLC data Admin-only | CR-002 | ADR-0004 | PlcEndpoints, WcsHub | TST-PLC-SEC-* (PlcRuntimeIntegrationTests) | IMPLEMENTED |
| PLC-07 | MC 3E frames equal the proven reference library byte-for-byte; response validated like the reference or stricter | CR-003 | MC_PROTOCOL_REFERENCE_ANALYSIS.md 2-4, 9 | McFrame, McTcpClient | TST-PLC-MC-PARITY (McReferenceParityTests) | IMPLEMENTED (mock); real PLC untested |
| PLC-08 | Devices M/X/Y/D/W; X never written; X/Y/W numbering base explicit per PLC, never defaulted | CR-003 | MC_PROTOCOL_REFERENCE_ANALYSIS.md 5 | DeviceAddress, AddressValidator, McPlcEndpoint | TST-PLC-MC-ADDR (McAddressTests, McCr003EndpointTests) | IMPLEMENTED; base NEEDS_CONFIRMATION |
| PLC-09 | A write without confirmation is ambiguous and never resent; reads retry once | CR-003 | MC_PROTOCOL_REFERENCE_ANALYSIS.md 6 | McTcpClient, PlcWorker | TST-PLC-MC-AMBIG (McCr003SocketTests, McCr003EndpointTests) | IMPLEMENTED (mock) |
| PLC-10 | Per-PLC connect/read/write timeouts and address bases, validated and Admin-only | CR-003 | MC_PROTOCOL_REFERENCE_ANALYSIS.md 8 | PlcConfigService, PlcConnection, PlcConfigTab | TST-PLC-MC-CFG (PlcMcIntegrationTests) | IMPLEMENTED |
| PLC-11 | Only approved addresses are polled/written; exchange hex visible to Admin only | CR-003 | MC_PROTOCOL_REFERENCE_ANALYSIS.md 9 | PlcRuntime, McPlcEndpoint, PlcWorker | TST-PLC-MC-APPROVED / TST-PLC-MC-DIAG (PlcMcIntegrationTests, McCr003EndpointTests) | IMPLEMENTED |
| PLC-12 | Connection parameters of the 4 PLCs are edited in exactly one screen, Admin only (API enforced); monitoring is read-only | CR-004 | CR-004 sections 2, 3, 6, 7 | PlcConnectionsPage, PlcPage, PlcEndpoints | TST-PLC-CR004-CONN (PlcCr004Tests; E2E 46/46) | IMPLEMENTED |
| PLC-13 | IO mapping for 368 IO: filters, D/W/M only, X/Y rejected, no auto-generated addresses, audited, per-PLC uniqueness | CR-004 | CR-004 sections 4, 5 | PlcIoMappingPage, PlcConfigService.SetAddressAsync | TST-PLC-CR004-IO (PlcCr004Tests) | IMPLEMENTED; X/Y OUT_OF_SCOPE / NEEDS_CONFIRMATION |
| PLC-14 | Entering an address never sets Pollable; only the approval mechanism does | CR-004 / CR-003 | CR-004 section 4 rule 10 | IoPointView.Pollable, StationIoPoint.IsPollable | TST-PLC-CR004-POLL (PlcCr004Tests) | IMPLEMENTED |
