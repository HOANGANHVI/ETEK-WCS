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

## Amendment 2026-11-07 – CR-003 (MC Protocol reference library)

11. **The codec is proven against the reference library.** The reference source is ported verbatim into the test project (`ReferenceOracle.cs`, test-only) and the WCS codec must produce identical bytes and identical accept/reject decisions. Product code does not reference it.
12. **Devices:** M, X, Y (bits) and D, W (words). X is read-only at every layer (codec, endpoint, validator). Proven per-request limits: 960 words, 3584 bits read, 1440 bits write.
13. **X/Y/W numbering base is a per-PLC engineer decision** (X/Y: 8 or 16; W: 10 or 16), never defaulted. The reference documents X/Y as octal but its code performs no conversion, and MELSEC-Q normally uses hexadecimal. Addresses are stored in the engineer's notation and converted only when the frame is built.
14. **Response validation:** subheader, announced length (hard cap 1922), echoed network/PC/module I/O/station, exact data length for successful answers. Any violation closes the socket (a late byte can never be matched to the next request).
15. **Ambiguous writes:** once a write may have reached the PLC without a confirming answer (timeout, disconnect, cancellation) the driver raises `PlcWriteAmbiguousException`; the worker reports `WRITE_AMBIGUOUS`, logs it and never resends. Reads are retried once on a fresh connection.
16. **Timeouts:** optional per-operation connect/read/write timeouts with fallback to the general timeout; the PLC monitoring timer (250 ms units) is explicit and never derived.
17. **Approved-only traffic:** a real PLC is read/written only at addresses that are entered and approved (`IoPointRef.Pollable`). The Admin still sees an entered but unapproved address.
18. **Diagnostics:** `PlcExchange` (command, device, start, points, duration, end code, request/response hex, ambiguity) per connection, Admin-only, hex capped at 96 bytes.
19. **Not ported:** `PingAsync` (reads D0 – an unapproved address), `IsHealthy`, DNS resolution, the reference's hard-coded module I/O/station, and its End Code descriptions (unverified).
