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
