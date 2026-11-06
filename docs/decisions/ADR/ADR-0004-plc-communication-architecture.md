# ADR-0004 – PLC communication architecture (CR-002)

Status: Accepted (architecture); protocol parameters NEEDS_CONFIRMATION
Date: 2026-11-06

## Context
Four physical PLCs serve 16 Stations, each Station with an independent instance of the common 23-signal IO template (368 IO points). Actual device addresses, TCP ports, MC parameters, STRING encoding/length, heartbeat scope and handshake rules are not yet confirmed by the PLC engineer.

## Decisions
1. **One connection and one worker per PLC** (4 TCP connections, never 16). The worker is the sole user of its connection; writes go through a queue and are never pipelined.
2. **`IPlcEndpoint` abstraction** with `McPlcEndpoint` (Mitsubishi MC 3E binary, proposed until confirmed) and `SimulatedPlcEndpoint`. Vendor code is isolated from business logic.
3. **One `StationIoHandler` per Station** holds value, quality and timestamp; it rejects foreign points. Identity key: `PLC_ID + Station_ID + Signal_Name`.
4. **No derived addresses.** `Actual_PLC_Address` is NULL until an Admin enters an engineer-confirmed value. Validation: per-type device rules and overlap detection within one PLC; the same address on different PLCs is allowed. Template addresses collide on every PLC (92 of 92), so they are never used as actual addresses.
5. **Three modes**: SIMULATOR (default), REAL_READ_ONLY, REAL_READ_WRITE. A live connection needs `AllowLiveConnection` and the PLC `Enabled`. READ_WRITE degrades to READ_ONLY unless write is enabled, an engineer approval reference is set, the handshake is approved, and every written point is confirmed, approved and on the write allow-list. Never switched automatically.
6. **Write policy**: `Safety_Ready`, `Transfer_Permit` and `AGV_Departure_Permit` are blocked from test writes. Handshake writes may bypass the block only in the Simulator and never for the Safety group.
7. **Handshake framework** is observational plus an approved-rule engine. No rules ship; each rule requires an approval reference. Until then WCS never acts on PLC signals.
8. **WCS integration** is opt-in (`Plc:Integration:Enabled` plus approved `StationFlows` mappings). A rising edge plus `Request_ID` creates one `StationRequest` (idempotent key `PLC:Station:RequestID`) through the existing flow. Unmapped Stations are monitor-only. The default `IStationSignalService` remains the legacy simulator.
9. **Realtime**: SignalR events; sensitive data (addresses, errors, logs) goes only to the `admins` group. Operators monitor only.
10. **Catalog**: `scripts/generate-plc-io-catalog.mjs` reads the controlled workbook into an embedded JSON (with sha256); seeding is additive and idempotent.

## Assumptions to confirm
INT is signed 16-bit; STRING is ASCII, low byte first, NUL-terminated; BOOL on M, INT/STRING on D; bit access subcommand 0x0001; `Request_ID` used as read.

## Consequences
Migration `AddPlcTopology` adds four tables (CREATE only) and is not auto-applied. Real-PLC behaviour is untested until commissioning.
