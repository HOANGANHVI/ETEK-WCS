# CR-027 – Dynamic AMR discovery and monitoring from the RCS (Phase 1)

- **Status:** Phase 1 `IMPLEMENTED` on `feature/cr-027-dynamic-amr-monitoring` (from `main` a0c4c70). Not merged, not deployed. **Phase 2 is NOT approved and NOT implemented.**
- **Source:** project owner instruction (2026-11-19) and the decisions A1–A3, B1–B4, C, D, E, F of the Phase 1 approval.
- **Baseline (never edited):** `CR-027_BASELINE.md`.
- **API reference:** `docs/03_RCS_Interface/UD35865B_RCS-2000 API_Developer Guide_V3.3_20231204.pdf` – RCS-2000 API Developer Guide **V3.3**, 2023-12-04, doc no. UD35865B, sha256 `8f73359f…76b63` (see `docs/03_RCS_Interface/README.md`).

## 1. Goal and scope

In Real mode the RCS manages and dispatches all AMRs. WCS only **queries and monitors** them: the number of AMRs is whatever the RCS returns (`data[]` of `queryAgvStatus`), identified by `robotCode`. WCS does not choose an AMR and does not create one.

**Phase 1 only changes the data source of the AGV Monitoring screen.** Phase 2 (letting the RCS choose the AMR when a task is created: dropping `agvCode` from `genAgvSchedulingTask`, nullable `AgvId`, redefining `PER_AGV`) is **deferred and not approved**.

## 2. Protected rules (unchanged, verified by tests)

`TransportTask.AgvId` and its foreign key (schema block of `transport_tasks`, `agvs`, `task_history`, `transport_orders`, `transport_legs`, `station_transactions`, `reservations`, `racks`, `locations`, `inventory_transactions`, `station_requests` is **byte-identical** to the baseline); `PER_AGV`; AGV1/AGV2 by role; `TaskOrchestrator`, `RouteEngine`, dispatch, `DispatchHold`, queues; `RcsCreateTaskRequest.RobotCode` and `genAgvSchedulingTask`; PLC, Station, Inventory, Rack binding; Dashboard / KPI formulas and every transport statistic.

## 3. What was verified against the PDF and what was not

| API | Section | Phase 1 | Status |
|---|---|---|---|
| `POST http://[address]:8182/rcms-dps/rest/queryAgvStatus` – request `reqCode` (req., ≤ 32), `reqTime`, `clientCode`, `tokenCode`, `mapShortName` (opt.); response `code` (Appendix A), `message`, `reqCode`, `data[]` (robotCode, robotDir, robotIp, battery, posX, posY, mapCode, speed, status, exclType, stop, podCode, podDir, path[]) | 5.2.14, pp. 46-48 | **implemented** (client + parser) | field names, types, required/optional and endpoint read from the PDF; **tested against a local test server and the simulator only – never against a real RCS-2000** |
| AMR Status codes | Appendix B, pp. 77-78 | labels for the documented codes (1-9, 11-18, 20, 21, 23-31, 33, 34) | codes 10, 19, 22, 32 are not in the document: shown as "code N (not in the API document)", never guessed |
| Status Code (`code`) | Appendix A, p. 76 | `0` = success; any other value is a failure `RCS_CODE_NOT_SUCCESS:<code>` with the RCS message | as documented (0 succeeded, 1 failed, 6 no need to resend, 99 unknown error) |
| `genAgvSchedulingTask` | 5.2.1 | **not implemented** (read only) | unchanged: Phase 2 |
| `queryTaskStatus` | 5.2.15 | not implemented | unchanged |
| `agvCallback` | 5.3.1 | not implemented | unchanged |

Points where the document is inconsistent (reported, not resolved by guessing): the request table lists `mapShortName` while the sample shows `mapCode`; the sample response repeats `robotCode` "1001" twice (the first entry is kept, the repeat counted as ignored); the sample shows `posX` "1.0" although the unit is millimetre; `queryAgvStatus` is on port 8182 while other APIs use `[:port]`. Phase 1 sends **only** `reqCode` (new 32-character id per query) and `reqTime`, plus `tokenCode` when the saved RCS connection uses token authentication. User/password authentication is not described in the document and is not used.

## 4. Design

**Read model (additive migration `AddRcsFleetSnapshots`)** – `rcs_amr_snapshots` (key `Source` + `RobotCode`; every reported field kept as the RCS sends it; `FirstSeenAt`, `LastSeenAt`, `UpdatedAt`) and `rcs_fleet_state` (one row per source: last attempt / success, ok flag, error code, consecutive failures, count, latency). It does not touch `agvs`. `Source` is `Simulator` or `Real`; the two are stored and read apart. **Rollback:** drop the two tables (`dotnet ef migrations remove` before release, or `DROP TABLE rcs_amr_snapshots, rcs_fleet_state` after) – no other table depends on them.

**Query** – `IRcsClient.QueryFleetAsync` (default: "not supported"; the simulator implements it: `SetFleetSize(n)`, `SetFleet`, `FleetFailure`, `Unreachable`). `IRcsFleetQuery` selects the source: `Rcs:Fleet:Source = Simulator` (default) or `Real` (`HikFleetQueryClient`: one read-only POST `queryAgvStatus`, host from the saved RCS connection setting – saved and enabled –, port 8182 and path from `Rcs:Fleet:Real`). It never creates, continues or cancels a task. Options (`Rcs:Fleet`): `Source`, `IntervalSeconds` (5), `TimeoutSeconds` (5), `StaleAfterSeconds` (0 = 3 intervals, min 15 s), `SimulatorSize` (0).

**Polling** – no new service. `AgvStatusHostedService` keeps its single loop; its tick is the fleet interval, the existing per-AGV refresh (`AgvStatusService.RefreshAsync`, unchanged) still runs about every 10 s, and `FleetService.RefreshAsync` runs each tick but queries only when the configured interval has passed. Timeout, HTTP error, refused connection and an invalid response are failures; the next interval is the retry.

**States** – per AMR: **Fresh** (in the last successful response and that response is recent), **Stale** (data kept but not refreshed: the last query failed or is older than the stale window), **Missing** (the last successful response did not contain it: the row is kept with its last-seen time, never deleted). Fleet connection: **Connected**, **Disconnected**, **NoData** (never queried). A failed query changes nothing in the stored AMRs and never creates one. Realtime event `FleetChanged`.

**API / UI** – `GET /api/agvs/fleet` (Admin and Operator; AMR IP and path only to Admin). AGV Monitoring gets the panel "AMR fleet reported by the RCS" (robot code, data state, RCS status with Appendix B label, battery, X/Y mm, direction, speed, excluded, stopped, carried rack, linked business AGV and active task read-only, last-seen time); a simulated list is labelled "Simulated data". The AGV1/AGV2 cards and the AGV summary stay unchanged. VI/EN.

## 5. Assumptions removed

The AGV Monitoring screen no longer depends on two AGVs: the fleet panel shows 0..N AMRs from the RCS and needs no seed. **Kept on purpose (Phase 2 scope):** the business AGV1/AGV2 rows, role assignment, `AgvId`, `PER_AGV`, `RcsRobotCode` in task creation, the 2-card AGV view and the per-AGV dashboard figure.

## 6. Tests

See `CR-027_RESULTS.md` (written after the final run). Test classes: `Cr027StatisticsBaselineTests` (golden, captured before any code change), `Cr027FleetTests` (fleet 0/1/2/3/25 and N, new / missing AMR, failures, stale, timeout, interval, simulator ≠ real, parser against the PDF sample, real client against a local server, regression: statistics identical for 0/1/2/3/40 AMRs, `agvs` / `AgvId` / task count unchanged, API visibility).

## 7. Not verified with a real RCS

Everything about `Source = Real` at run time: the real HTTP exchange, authentication (token placement), the response of an actual RCS-2000, port 8182 reachability, behaviour with 100+ AMRs and the polling frequency recommendation under real load, the meaning of `status` codes in the field, `path` content. Phase 2 items are untouched.

## 8. Configuration (no deployment file was changed)

`Rcs:Fleet:Source` / `IntervalSeconds` / `TimeoutSeconds` / `StaleAfterSeconds` / `SimulatorSize` / `Real:Port` / `Real:Path` (environment form `Rcs__Fleet__Source` …). Defaults keep the system exactly as before: simulator source, empty simulated fleet. `docker-compose.yml` and `.env` are not modified; to use the real source an operator sets `Rcs__Fleet__Source=Real`, saves and enables the RCS connection in System Settings, and restarts the backend.
