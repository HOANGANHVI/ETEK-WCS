# CR-031 – Real RCS fleet monitoring, no simulated AMR data in operation

| | |
|---|---|
| **Status** | `REAL RCS FLEET MONITORING – NO SIMULATED AMR DATA IN PRODUCTION` is implemented and **mock verified**. **Real acceptance NOT done**: this machine cannot reach the RCS (see section 6). |
| **Branch** | `feature/cr-031-real-fleet-monitoring` (from `main` `f64a8b0`). Not merged. |
| **Source** | RCS-2000 API Developer Guide V3.3, 5.2.14 `queryAgvStatus` (`POST /rcms-dps/rest/queryAgvStatus`) |
| **Scope** | Monitoring only. No Real Dispatch, no transport command, no PLC write, no Inventory/Rack change, Legacy dispatch unchanged. |

## 1. What was found (CR-027 already had the real client; four defects made simulated data visible)

1. The **business AGV cards** (AGV1 / AGV2 from the `agvs` table) were shown next to the real fleet list and their status came from the RCS **simulator** ("Online – simulated").
2. The fixed default port **8182** was assumed instead of the port of the saved RCS connection.
3. A token connection without a stored token still sent the request (no configuration error).
4. The poll was not logged per cycle; a tick that arrived a few milliseconds early skipped the poll (effective 10 s instead of 5 s); a manual refresh could overlap the loop.

## 2. Changes

| Area | Change |
|---|---|
| Source | `Rcs:Fleet:Source=Real` selects `HikFleetQueryClient` **at start**: no fallback to the simulator at runtime, ever. The count of AMRs is the number of elements of `data[]`; nothing is fixed or invented. |
| Port / auth | The port is the **saved RCS connection** port (`Rcs:Fleet:Real:Port` is only an override; default 0; if none is set: configuration error, no port assumed). **Authentication (project decision, 2026-11-21): the RCS provides NO authentication for the monitoring API.** `POST /rcms-dps/rest/queryAgvStatus` is called **without username, password or `tokenCode`**: the body is only `reqCode` and `reqTime`; polling never depends on a token, never fails for a missing token, and WCS never generates a token or simulates authentication. A token/password that may still be saved in the RCS connection is ignored by the monitoring client. (The API document defines `tokenCode` as optional; the site has confirmed it is not used.) |
| Business AGV status | While the fleet is the real RCS, `AgvStatusService` never asks the simulator: AGV rows get `Unknown` / `RCS_FLEET_REAL` (the persisted "Online – simulated" is replaced). |
| UI | With a Real source the AGV Monitoring and Dashboard show **one list from the RCS** (no AGV1/AGV2 cards, no per-AGV history, no AGV counters); the connection pill reads **RCS DISCONNECTED** with the last successful query time; stored data that is no longer refreshed is Stale / Missing, never Live. |
| Logs | Every executed poll: `RCS_FLEET_POLL_STARTED` (source, interval, timeout) then `RCS_FLEET_POLL_SUCCESS` (HTTP status, DurationMs, TotalAmr, RobotCodes, last successful poll) + `RCS_FLEET_SNAPSHOT_UPDATED`, or `RCS_FLEET_POLL_FAILED` (ErrorCode, HTTP status, sanitised message, ConsecutiveFailures). Token/password/secret patterns are masked; the client never writes a credential. |
| Polling | 5 s (`Rcs:Fleet:IntervalSeconds`), timeout `Rcs:Fleet:TimeoutSeconds` (5), single loop, **single-flight** gate (no overlap), tolerance for early ticks, continues after any error. |
| Production | `ProductionSafety.ValidateFleet`: Production **refuses to start** with a simulated fleet unless `Rcs:Fleet:AllowSimulatorInProduction=true`. Deployment default `RCS_FLEET_SOURCE=Real`. |

Not changed: the simulator remains for development; business `agvs` rows still exist for **Legacy dispatch** (role-based) and are simply not shown as AMR data in Real mode; no Real Dispatch.

## 3. Tests (MOCK)

`Cr031RealFleetMonitoringTests` (16 cases) + the CR-027 suite (32) = 48/48: 0, 1, 2, 5 AMR responses with the simulator holding AMRs that never appear; only returned fields shown (nothing invented), battery/position/status changes followed; AMR that disappears → Missing, row kept; timeout → RCS DISCONNECTED, last good time kept, old data not Fresh; loss (503) and recovery, failures reset, one poll per interval also after failures; switched-off / missing connection → configuration error, nothing sent; no credential of any kind in the request whatever the saved mode (token, username/password, none), polling works with no secret saved, no secret in a log; log sequence and content; 5 s cadence with an early tick and no overlap; business AGV status never from the simulator; Production guard. A mock is **never** operational data.

## 4. Deploy-machine configuration to confirm (not in Git)

RCS **host** and **port** (the values saved in *Kết nối RCS*; expected port 8994, not hard-coded; the document's example for this path is 8182), `RCS_FLEET_SOURCE=Real`, `RCS_FLEET_INTERVAL_SECONDS=5`. **No username, password or token is required.** Enter host/port in the UI on the deploy machine; no address or credential belongs in Git.

## 5. Evidence available now (dev machine, real runtime, saved RCS connection 192.168.10.11:8994)

The backend was started with `Rcs__Fleet__Source=Real` and logged, every 5 s: `RCS_FLEET_POLL_STARTED (source Real …)` → `RCS_FLEET_POLL_FAILED: RCS_TIMEOUT, HTTP null, No answer within 5 s., consecutive failures n`. That proves WCS **actively calls** the real client in the runtime. It also shows the RCS did **not answer**: the dev machine (192.168.30.7 / 172.16.0.2) is not on the RCS network.

## 6. Acceptance status

| # | Item | Status |
|---|---|---|
| 1 | Real RCS client used at runtime | **Yes** (log `source Real`) |
| 2 | WCS sent `queryAgvStatus` | **Yes** (attempts logged; mock confirms path/body) – the real RCS did not receive it from this machine |
| 3 | RCS returned a real response | **No – RCS unreachable (timeout)** |
| 4 | Number of AMRs received | **Not available (0 real responses)** |
| 5 | Frontend shows the response | Verified with the mock only; with the real RCS: **not possible yet**. The screen shows RCS DISCONNECTED and the empty / stale state |
| 6 | Real polling log | Attempts and timeouts only |
| 7 | | **Real acceptance cannot be declared**: it needs a machine in the RCS network, the confirmed port (no authentication is used) |

## 7. Fix: RCS non-success response was not found in the backend log (2026-11-23)

**Symptom (deploy machine).** AGV Monitoring: "Mất kết nối RCS … lần cập nhật cuối lúc – … RCS báo không thành công (RCS_CODE_NOT_SUCCESS:1)", no matching line in the backend log.

**Where the code comes from.** `RCS_CODE_NOT_SUCCESS:<n>` is produced by `HikQueryAgvStatusParser` when the RCS answers with an **HTTP success status** but the JSON field `code` is not `"0"`. (An HTTP failure is `RCS_HTTP_ERROR:<status>`.) RCS-2000 API V3.3 Appendix A: `0` Succeeded, **`1` Failed – "refer to the field message for detailed error information"**, `6` no need to resend, `99` unknown error, try again. Nothing beyond that is claimed: the meaning of "1" for `queryAgvStatus` is only what the document says; the reason is in the RCS `message`.

**Why there was no log (three causes).**
1. The first implementation of the fleet loop logged only a **transition** (success → failure). A fleet that has **never** succeeded never had a "previous success", so it logged nothing (the screen showed "–" for the last update).
2. CR-031's per-poll warning is a Warning: the log pipeline's `ThrottlingSink` (60 s window, key = source + template + first 120 characters of the rendered text) keeps one line per minute and counts the rest, so a poll every 5 s was mostly hidden.
3. The RCS `message`, the endpoint, the HTTP status and the duration were never part of the line.

**What changed.**
- **Every poll ends with one line** on stdout / file: `RCS_FLEET_POLL_STARTED`, then `RCS_FLEET_POLL_SUCCESS` or `RCS_FLEET_POLL_FAILED` (+ `RCS_FLEET_SNAPSHOT_UPDATED` on success). They carry the property `NoThrottle`, so the 60-second throttle never hides a cycle. Fields: timestamp, endpoint (`host:port/path`, no credential exists in this call), HTTP status, RCS code, RCS message (the two checked fields only: bounded to 200 characters, credential-like text masked, the body is never logged), DurationMs, request id (`reqCode`), ConsecutiveFailures. String values are written literally (`{x:l}`) so they can be searched.
- **Error classes:** `RCS_TIMEOUT` (no answer in time; the client times out 250 ms before the caller so the line has the endpoint and request id), `RCS_CONNECTION_FAILED` (**new**: refused / no route / DNS), `RCS_HTTP_ERROR:<status>`, `RCS_CODE_NOT_SUCCESS:<code>` (HTTP success, RCS business code not "0"), `RCS_RESPONSE_INVALID` (not JSON, not an object, no `code`, `data` not a list), plus `RCS_NOT_CONFIGURED`.
- **Data state:** a non-success response is **not** a successful poll: no snapshot update, `LastSuccessAt` unchanged, the last good snapshot is kept and becomes Stale / Missing, never Live. With **no success ever** the API has no update time and the screen shows **"Chưa nhận được dữ liệu AMR từ RCS"** (no dash, no made-up time) plus the error code and the RCS message.
- **system_log (searchable copy):** rate-limited by design so a 5-second poll does not fill the table: a failure is stored when it is the first, its code changed, or 60 s after the last stored one; a success only after a failure or 60 s after the last stored one; STARTED and SNAPSHOT_UPDATED are console / file only (`NoPersist`).

**Where the log is.** (1) **stdout of the backend container**: `docker compose logs backend | findstr RCS_FLEET` (compose has no `Logging:File:Path`; set it for a file copy). (2) The **system log table** (screen *Nhật ký hệ thống*, search `RCS_FLEET`), rate-limited as above. `C:ETEK-WCSlogs` holds only the **deploy script** logs (imported read-only into the log screen); it is **not** where the backend writes polling logs.

**Example (real runtime on the dev machine, RCS unreachable):**
`RCS fleet poll failed: RCS_TIMEOUT (The RCS did not answer in time.), endpoint 192.168.10.11:8994/rcms-dps/rest/queryAgvStatus, HTTP null, RCS code null, RCS message null, 4764 ms, request 9a2d93…, consecutive failures 187. RCS_FLEET_POLL_FAILED`

**Verification.** `Cr031PollOutcomeTests` (16, MOCK RCS; events pass through the real `ThrottlingSink`): HTTP 200 + code 1, HTTP 200 + code 0, timeout, refused connection, HTTP 404/500/503, invalid bodies, repeated failures (all logged, counter grows), failure after success (snapshot and time kept, Stale), no success ever, every cycle one SUCCESS/FAILED, stored-copy rate limit. With `Cr031RealFleetMonitoringTests` and `Cr027FleetTests`: 63/63. Real backend run: 8 STARTED / FAILED lines in 40 s, none suppressed, no simulator data, one stored row in `system_log` for several minutes.

**Not verified:** the **real** RCS response (the environment cannot reach the RCS), so the actual `code`/`message` that produced `RCS_CODE_NOT_SUCCESS:1` on the deploy machine is **not observed**; after deploying this fix the line `RCS_FLEET_POLL_FAILED` will show it.
