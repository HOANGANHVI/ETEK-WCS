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
