# CR-033 – RCS integration logging: category classification fix

| | |
|---|---|
| **Status** | Implemented on `main` (baseline `f8ce9b8`). Tests: focused set passes. **No migration. No change to existing rows.** Not deployed. |
| **Scope** | Which category a *new* system-log event gets. No change to PLC handshake, WCS workflow, RCS polling logic or robot commands. No change to the query API, the KPI or the screen. |

## 1. Pipeline audited

`Logger / producer` → `LogClassifier.Classify` (Serilog sink `SystemLogSink`) → table `system_log` (column `Category`, index `(Category, OccurredAt)`) → view `v_log_entries` (system_log + plc_comm_log + audit_log) → `LogQueryService` (category filter, paging, `StatsAsync` KPI = `GROUP BY Category` over the same view) → `/api/logs`, `/api/logs/stats` → Nhật ký hệ thống screen; the diagnostic export (`SystemLogSource`) copies the stored `Category` and derives `Module` from the event code.

The query, the KPI, the paging and the export read ONE column, so they are consistent. The only wrong layer was the **producer-side classification**.

## 2. Root cause

`LogClassifier.Classify` decided RCS / AGV from the **logger name** only (`AgvStatus`, anything containing `Rcs`, `TaskOrchestrator`+“RCS”). `FleetService` (`WCS.Application.Fleet.FleetService`) has neither, so `RCS_FLEET_POLL_SUCCESS`, `RCS_FLEET_QUERY_RECOVERED`, `RCS_FLEET_POLL_FAILED`, … fell through to `SYSTEM`. The export *module* is derived from the event-code prefix (`RCS_` → `Rcs`), which is why the same row read `category = SYSTEM`, `module = Rcs`. The same gap applied to `RCS_NOT_CONNECTED` (logged by `WCS.Api`) and to any other class not named “…Rcs…”. The RCS / AGV KPI therefore counted 0 for fleet events.

`SECURITY_AUDIT` for the RCS Diagnostics actions is **correct**: those are rows of `audit_log` (`RCS_DIAG_EXECUTE`, `RCS_DIAG_TCP_CHECK`, `RCS_DIAG_PARAM_SAVE`, `RCS_DIAG_EXPORT`, `CONFIG_RCS_UPDATE`, `CONFIG_RCS_TEST`), written by the audit service as what a person did. They are not logger events and are not touched.

## 3. Rule

| Group | Decided by | Examples |
|---|---|---|
| **RCS / AGV** | the **event code**: `RCS_*` (integration) and `AGV_STATUS*` (AMR status loop), whichever class writes it; plus the existing logger-name rules (`AgvStatus`, `*Rcs*`, `FleetService`, `TaskOrchestrator`+RCS) for events without a code | `RCS_FLEET_POLL_SUCCESS`, `RCS_FLEET_QUERY_RECOVERED`, `RCS_FLEET_POLL_FAILED`, `RCS_CODE_NOT_SUCCESS`, `RCS_TIMEOUT`, `RCS_CALL_EXCEPTION`, `RCS_NOT_CONNECTED` |
| **SECURITY_AUDIT** | unchanged: rows of `audit_log`; Auth / Audit / Bootstrap loggers | sign-in / out, `RCS_DIAG_*` actions, `CONFIG_RCS_*`, permissions |
| **SYSTEM** | unchanged: everything else | startup / shutdown, scheduler, services not about the RCS |
| PLC, DATABASE, DEPLOYMENT | unchanged (explicit `Category` property first, then `DB_*` / database exceptions) | |

An explicit `Category` property on the event still wins over everything. The word “RCS” in a message, a module name or a code that merely *contains* RCS (`TASK_RCS_NOTE`) does **not** change the category. The rule lives in one function (`LogClassifier.IsRcsAgvEventCode`); there is no frontend rule that re-classifies by message text.

## 4. Structure of the RCS events (audit result – no change needed)

The RCS fleet events already carry endpoint (host:port + path), HTTP status, RCS code and message, duration, AMR count / robot codes, request id, error code / message and the consecutive-failure count (message + `DetailsJson`), the event name as `EventCode`, severity as `Level`, and `CorrelationId` when present. Secrets are masked by `LogSanitizer` before storage; no token, password or body is logged (CR-031 tests). Not changed.

**Log volume (not changed, reported).** One console line per poll (`RCS_FLEET_POLL_STARTED`, `…_SUCCESS`, `…_SNAPSHOT_UPDATED`) is written every 5 s by design of CR-031, and CR-031 tests assert it. Only the **database** copy is throttled (a success every 60 s or on a state change; failures when new / changed / every 60 s; started and snapshot lines are never stored). Lowering the console level or sampling it would change CR-031 behaviour and is out of scope here; it can be a separate decision.

## 5. Historical rows – is a backfill needed?

Rows written before this change keep `SYSTEM` (they are **not** modified by this CR). New events are classified correctly from the moment of deployment. The only affected population is `system_log` rows with `Category = 'SYSTEM'` and an RCS / AGV_STATUS event code. A backfill is optional; **it was NOT created as a migration and NOT run**. If the owner wants the history corrected, run this as a reviewed one-off (after a backup), with the counts checked before and after:

```sql
-- 1. how many rows would change (and which codes)
SELECT "EventCode", count(*) FROM system_log
WHERE "Category" = 'SYSTEM' AND ("EventCode" LIKE 'RCS\_%' ESCAPE '\' OR "EventCode" LIKE 'AGV\_STATUS%' ESCAPE '\')
GROUP BY "EventCode" ORDER BY 2 DESC;

-- 2. the change: only the Category column; time, message, level, source, device, details are untouched
BEGIN;
UPDATE system_log SET "Category" = 'RCS_AGV'
WHERE "Category" = 'SYSTEM' AND ("EventCode" LIKE 'RCS\_%' ESCAPE '\' OR "EventCode" LIKE 'AGV\_STATUS%' ESCAPE '\');
-- compare the row count with step 1, and `SELECT count(*) FROM system_log` before / after (must be equal); then COMMIT (or ROLLBACK)
```

The condition is by event code only (never by module or text). `audit_log` and `plc_comm_log` are not involved.

## 6. Tests

`tests/WCS.Tests/Logging/Cr033RcsLogCategoryTests.cs` (11): the RCS event codes (fleet success / recovered / failed, code-not-success, timeout, exception, not-connected, AGV status) are RCS / AGV whichever class writes them and keep their event name; the word RCS in a message, module or logger does not re-classify; DATABASE / SECURITY_AUDIT / DEPLOYMENT / explicit `Category` / PLC (not stored) unchanged; module derived independently of the category; through the real sink and the database: category filter returns exactly the RCS events and the audit rows stay in SECURITY_AUDIT, source filter works independently and combined, KPI counts each row once (sum = total), paging totals are consistent, and the diagnostic export keeps `category` and `module`.
