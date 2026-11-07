# Testing Report – CR-002 PLC communication

Date: 2026-11-06

## Status (reported separately)
| Item | Status |
|---|---|
| SOURCE CODE IMPLEMENTED | YES |
| SIMULATOR / MOCK TESTED | YES |
| REAL PLC TESTED | **NO** – no physical PLC was available |

## Results
- Backend: `dotnet test src/backend/WCS.sln` – **302 passed, 0 failed** (220 before CR-002; no earlier test removed or weakened).
- Frontend: eslint clean; `tsc -b` clean; `npm run build` and `npm run build:demo` succeed.
- Browser E2E (Chrome, Simulator mode, scratch DB): 39/39 – 4 PLC cards, 16 stations, 23 IO rows, address dialog and conflict, blocked permit signals, simulator test-write, PLC-01 offline isolation and reconnect, Operator view without IP/address and 403 on admin APIs.

## Coverage (TST-PLC-*)
| Area | File |
|---|---|
| Config, address validation, write policy | Plc/PlcConfigTests.cs, Integration/PlcConfigIntegrationTests.cs |
| MC 3E codec (byte vectors) and TCP client vs mock server | Plc/McProtocolTests.cs, Plc/McEndpointTests.cs |
| 4 workers, isolation, reconnect, write queue | Plc/PlcWorkerTests.cs |
| Simulator runtime, modes, SignalR admin routing | Integration/PlcRuntimeIntegrationTests.cs |
| Handshake framework, signal service | Plc/HandshakeTests.cs, Integration/PlcWcsIntegrationTests.cs |

## Not covered
Real PLC wire behaviour, QJ71E71-100 settings, real timing, INT sign / STRING encoding, handshake with real signals, Demo Mode PLC data.

---

# CR-003 – MC Protocol reference library integration (2026-11-07)

## Status (reported separately)
| Item | Status |
|---|---|
| SOURCE CODE IMPLEMENTED | YES |
| SIMULATOR / MOCK TESTED | YES |
| REAL PLC TESTED | **NO** – no physical PLC |

## Results
- Backend: **389 passed, 0 failed** (302 before CR-003 + 87 new). No existing test was removed or weakened.
- Socket/timing tests repeated 5 times with no failure.
- Frontend: eslint, `tsc -b`, `npm run build`, `npm run build:demo` clean.
- Browser E2E (Chrome, Simulator, scratch DB): CR-002 regression suite 41/41; CR-003 suite 13/13 (new fields, base selects unconfirmed by default, range errors, X010 stored as X10, W refused while its base is unconfirmed).
- Not browser-verified: the Admin "Giao tiếp gần nhất" hex box (type-checked and built; its payload is asserted by `PlcMcIntegrationTests`).

## Coverage (TST-PLC-MC-*)
| Area | Tests |
|---|---|
| Byte-for-byte parity with the reference (requests, nibbles, responses accept/reject) | McReferenceParityTests |
| Addresses, radix, validator, approved-only polling (unit) | McAddressTests |
| Real TCP against the mock: W/X/Y, strict header checks, length attacks, partial/closed responses, retry rules, end code, ambiguous writes, per-operation timeouts, concurrency, diagnostics hex | McCr003SocketTests |
| Endpoint and worker: bases, X write refusal, ambiguous write, unapproved address, Admin-only address | McCr003EndpointTests |
| API + PostgreSQL: new parameters, base gate, approved-only polling, Admin-only diagnostics | PlcMcIntegrationTests |

## Not covered (needs the physical PLC)
Behaviour of the QJ71E71-100, real timing and End Codes, the real X/Y/W numbering base, per-request limits above the proven ones, real addresses, handshake.

---

# CR-004 – PLC configuration reorganization (2026-11-07)

## Status (reported separately)
| Item | Status |
|---|---|
| SOURCE CODE IMPLEMENTED | YES |
| SIMULATOR / MOCK TESTED | YES |
| REAL PLC TESTED | **NO** – no PLC involved |

## Results
- Backend: **397 passed, 0 failed** (389 + 8 new in `PlcCr004Tests`). One CR-003 test (`PlcMcIntegrationTests`) was deliberately changed: it used X addresses through the API, which CR-004 now rejects; its X/Y-base-gate part now asserts the rejection and exercises the same storage/overlap rules with W.
- Frontend: eslint, `tsc -b`, `npm run build`, `npm run build:demo` clean.
- Browser E2E (Chrome, Simulator, scratch DB): 46/46 – menu groups, system config without PLC tab, read-only monitoring, 4 PLC connection forms (PLC-01 edit leaves PLC-02/03/04 unchanged), 368-row IO mapping, PLC/Station/status/search filters, X10/Y10 rejected, M100/D2000 saved, entered is not pollable, approval makes it pollable, same-PLC duplicate refused, other-PLC duplicate accepted, conflict check, Operator denied (routes and API 403).

## CR-004 acceptance and production UI cleanup (2026-11-07)

| Item | Status |
|---|---|
| SOURCE CODE IMPLEMENTED | YES |
| SIMULATOR / MOCK TESTED | YES |
| REAL PLC TESTED | **NO** |

- Backend: **398 passed, 0 failed** (397 + 1 new: engineering tools allowed in Development, refused with 409 in Production, configuration still available to Admin).
- Frontend: eslint, `tsc -b`, `npm run build`, `npm run build:demo` clean.
- Browser (Chrome, scratch DB, headless): cleanup suite 10/10 (header status, monitoring "Chưa kết nối", engineering buttons present only in the dev server for Admin, Operator and production-build Admin sweeps of 12 routes free of demo/simulation wording and tool buttons, 368 IO rows, operator API 403); CR-004 regression 44 PASS / 0 FAIL.
- Visual: screenshots of Operator monitoring, Admin monitoring and IO mapping (production build) reviewed.
- Excluded from the wording sweep by design: audit trail and PLC communication log (recorded history).

---

# CR-006 – Dockerization and Windows deployment (2026-11-07)

| Item | Status |
|---|---|
| SOURCE CODE IMPLEMENTED | YES |
| SIMULATOR / MOCK TESTED (backend logic) | YES |
| DOCKER IMAGE BUILD | **NOT RUN** (no Docker on the development machine) |
| CONTAINER RUNTIME / PERSISTENCE / BACKUP-RESTORE / SIGNALR VIA NGINX | **NOT RUN** |
| REAL PLC TESTED | **NO** |

## Executed
- Backend: **409 passed, 0 failed** (398 + 11 new in `Cr006DeploymentTests`: cookie policy resolution and refusal of None/invalid; Production start-up guard matrix; Production host refuses the simulator by default and starts with the explicit exception; `/healthz` anonymous with application/database fields). Three existing tests that use the Production environment (`AuthIntegrationTests.Default_admin_is_not_created_in_Production...`, `PlcCr004Tests.Engineering_tools...`) now set the explicit simulator exception in their set-up; no assertion changed.
- Frontend: eslint, `tsc -b`, `npm run build` clean.
- Static checks: all 8 PowerShell scripts parse without errors and are ASCII-only (Windows PowerShell 5.1 safe); compose YAML parsed - 4 services, only the frontend publishes a port, `db_net` internal, no privileged/host network, every variable used exists in `.env.example`; env validation logic of the scripts exercised with 6 cases (empty password, Simulator refused/allowed, bad cookie value, defaults, missing file).
- Outside containers, with the production-like environment of the compose file: an EF migration bundle built (`dotnet ef migrations bundle`) and applied all 5 migrations to an empty PostgreSQL database in Production environment; the published API started in Production with `Plc:Mode=RealReadOnly`, `AllowLiveConnection=false`: `/healthz` 200 anonymous, `/api/system/health` 401 anonymous, login with the bootstrap password (default `admin/admin` rejected), cookie not Secure with `SameAsRequest`, PLC runtime `RealReadOnly` with all 4 PLC `Disabled` (no socket, no simulator), simulator endpoint 409 in Production, forwarded client IP recorded in the audit trail.

## Not executed (needs Windows 11 + Docker Desktop)
Everything in `docs/DEPLOYMENT_TEST_CHECKLIST.md` (A1-F2): image builds, compose config, container health, Nginx SPA refresh, SignalR WebSocket through Nginx, restart persistence, backup/restore, Git-based deploy/update/rollback, network isolation, route from the backend container to the PLCs (needs the confirmed TCP port), real PLC communication.

## CR-006 completion checks (2026-11-07)
- Backend **410 passed, 0 failed** (adds the forwarded-header trust test).
- Outside containers (published app, Production environment, scratch DB, dropped afterwards): trust enabled without a proxy list -> process refuses to start; request with `X-Forwarded-For: 10.9.8.7` from a sender NOT in the trusted list -> audit IP stays the real peer (::1); from a trusted sender -> 10.9.8.7 recorded.
- Still NOT run: Docker build/runtime, Nginx header overwrite, network isolation, persistence, backup/restore (checklist A-G).
