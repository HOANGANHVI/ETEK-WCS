# ADR-0012 – Real RCS dispatch adapter (CR-030)

- **Date:** 2026-11-21 · **Status:** accepted and implemented behind a switch that is OFF; verified with a MOCK RCS only; pending real RCS commissioning.
- **Related:** ADR-0009 (fleet monitoring), ADR-0010 (RcsManaged dispatch), ADR-0011 (PLC interface decisions), `docs/change-management/CR-030_REAL_RCS_DISPATCH.md`.

## Decisions
1. **Three separate paths:** the simulator (default, unchanged), real fleet monitoring (CR-027, read-only `queryAgvStatus`), real fleet dispatch (CR-030, `HikRcsClient`). `Rcs:RealDispatch:Enabled` (default false) is the only switch; nothing switches Simulator to Real automatically.
2. **Document first, no guessing.** The client follows RCS-2000 API V3.3: `genAgvSchedulingTask`, `queryTaskStatus`, `continueTask`, `cancelTask`, and the RCS → WCS `agvCallback`. Where the document is silent (callback authentication, what each BP template needs in `positionCodePath`, `taskTyp`) nothing is invented: the setting is empty and the call **fails closed**.
3. **Mapping is configuration** (`Rcs:RealDispatch:Locations`, `Processes`, optional JSON file outside Git). Empty by default; missing mapping → nothing is sent, a clear error names the missing item; no `positionCode`/`taskTyp` is generated or hard-coded.
4. **RCS chooses the AMR** (CR-028 kept): no `agvCode` in RcsManaged requests; the AMR is learned from `queryTaskStatus` and the callback; "sent ≠ assigned". The WCS request code is the RCS `taskCode`; `reqCode` is a deterministic derivation (≤ 32), so a repeated request keeps the same `reqCode`.
5. **Outcome semantics:** `0` and `6` accepted; `1` certain failure; `99`, undefined codes, HTTP errors, timeouts, connection errors and unreadable answers are **unknown** (never a certain failure; the retry first asks the RCS).
6. **Callback authentication is not defined by the API document.** None is invented as "RCS compatible". WCS refuses callbacks unless a shared secret (header) and/or allowed source addresses are configured, all configured checks must pass, and the route is not mapped unless the real dispatch and its callback are enabled. A callback records the robot and the AMR state; it **never completes a task, confirms a pick/drop or changes Rack/Inventory** (CR-028 section 13, D-4).
7. **Routing legs are not dispatched with the real RCS** (no confirmed leg → task-type mapping): `RealLegDispatcher` blocks them; the simulator API is not exposed in real mode.
8. **No database change.** Callback deduplication reuses `processed_events`. Rollback is configuration only.

## Consequences
A deployment can be prepared (mapping file, secrets from the environment) without sending anything; commissioning with the RCS engineer is required before Real Dispatch is enabled (CR-030 section 10). Vendor questions 16–21 list what the document does not settle.
