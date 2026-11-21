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
| Port / auth | The port is the **saved RCS connection** port (`Rcs:Fleet:Real:Port` is only an override; default 0; if none is set: configuration error, no port assumed). `tokenCode` is sent for token authentication; a token connection with no stored token is a configuration error. A saved *username/password* connection is **not defined by API V3.3**: the request is sent without credentials and a warning `RCS_AUTH_MODE_NOT_IN_API` asks to confirm the authentication with the RCS engineer. |
| Business AGV status | While the fleet is the real RCS, `AgvStatusService` never asks the simulator: AGV rows get `Unknown` / `RCS_FLEET_REAL` (the persisted "Online – simulated" is replaced). |
| UI | With a Real source the AGV Monitoring and Dashboard show **one list from the RCS** (no AGV1/AGV2 cards, no per-AGV history, no AGV counters); the connection pill reads **RCS DISCONNECTED** with the last successful query time; stored data that is no longer refreshed is Stale / Missing, never Live. |
| Logs | Every executed poll: `RCS_FLEET_POLL_STARTED` (source, interval, timeout) then `RCS_FLEET_POLL_SUCCESS` (HTTP status, DurationMs, TotalAmr, RobotCodes, last successful poll) + `RCS_FLEET_SNAPSHOT_UPDATED`, or `RCS_FLEET_POLL_FAILED` (ErrorCode, HTTP status, sanitised message, ConsecutiveFailures). Token/password/secret patterns are masked; the client never writes a credential. |
| Polling | 5 s (`Rcs:Fleet:IntervalSeconds`), timeout `Rcs:Fleet:TimeoutSeconds` (5), single loop, **single-flight** gate (no overlap), tolerance for early ticks, continues after any error. |
| Production | `ProductionSafety.ValidateFleet`: Production **refuses to start** with a simulated fleet unless `Rcs:Fleet:AllowSimulatorInProduction=true`. Deployment default `RCS_FLEET_SOURCE=Real`. |

Not changed: the simulator remains for development; business `agvs` rows still exist for **Legacy dispatch** (role-based) and are simply not shown as AMR data in Real mode; no Real Dispatch.

## 3. Tests (MOCK)

`Cr031RealFleetMonitoringTests` (15 cases) + the CR-027 suite (32) = 47/47: 0, 1, 2, 5 AMR responses with the simulator holding AMRs that never appear; only returned fields shown (nothing invented), battery/position/status changes followed; AMR that disappears → Missing, row kept; timeout → RCS DISCONNECTED, last good time kept, old data not Fresh; loss (503) and recovery, failures reset, one poll per interval also after failures; switched-off / missing connection → configuration error, nothing sent; token only as `tokenCode` and never in a log; username/password sends no credentials; log sequence and content; 5 s cadence with an early tick and no overlap; business AGV status never from the simulator; Production guard. A mock is **never** operational data.

## 4. Deploy-machine configuration to confirm (not in Git)

RCS **host**, **port** (the value saved in *Kết nối RCS*; the document's example is 8182 for this path), **authentication** (token provided by RCS-2000; the dev database holds a username/password connection that the API does not define), `RCS_FLEET_SOURCE=Real`, `RCS_FLEET_INTERVAL_SECONDS=5`. Enter host/token in the UI on the deploy machine; never in Git or chat.

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
| 7 | | **Real acceptance cannot be declared**: it needs a machine in the RCS network, the confirmed port and the authentication |
