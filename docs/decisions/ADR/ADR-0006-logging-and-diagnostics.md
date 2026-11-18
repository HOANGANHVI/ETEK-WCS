# ADR-0006 – Logging and diagnostics

Status: accepted (CR-007, 2026-11-16).

## Context
Logs existed in three unconnected places (Serilog stdout, `audit_log`, `plc_comm_log`); PLC events never reached stdout, start-up failures were anonymous stack traces, a database outage flooded the log, deployment scripts left no trace and no screen could search any of it.

## Decisions
1. **One screen, three stores, no duplication.** `system_log` (new) holds SYSTEM / DATABASE / DEPLOYMENT / RCS_AGV events; `plc_comm_log` and `audit_log` keep their events. A read-only view `v_log_entries` presents all three; filtering, ordering and paging run in PostgreSQL.
2. **Serilog stays the producer.** A best-effort sink copies Warning+ and EventCode-tagged events to `system_log`; it never throws into the caller, keeps entries while the database is down, isolates an un-storable entry, never persists PLC events or its own storage events, and stdout is always written.
3. **PLC events** are mirrored and enriched (IP/port, previous/new state, timeout / end-code kind, reconnect duration) inside `PlcEventDispatcher`; `PlcWorker` and the MC driver are untouched.
4. **Throttling** (first occurrence, then one per window with a suppressed count) is a Serilog pipeline component; PLC events bypass it.
5. **Secrets:** `LogSanitizer` masks on write and on read; the PowerShell scripts mask known .env secret values and secret patterns.
6. **Deployment events:** scripts write text + JSONL logs to `C:\ETEK-WCS\logs`; the folder is mounted read-only into the backend and imported idempotently (unique event id). No ingestion endpoint is exposed.
7. **Access:** every log endpoint is Admin-only and read-only; no endpoint writes or deletes log data.
8. **Retention:** `system_log` 90 days (floor 7) by a maintenance service; `plc_comm_log` 30 days (unchanged); `audit_log` not purged; deployment log files are never deleted automatically (`prune-logs.ps1` is manual, dry run by default).
9. **Start-up diagnostics:** structured `STARTUP_FATAL` and `STARTUP_SUMMARY` (no secrets); request logging precedes the exception handler so status codes are truthful.

## Consequences / limits
Events produced while the database is down are in stdout (and in the queue, up to 2000 entries, until it returns). The view sorts three tables at query time; measured ≈0.2 s per page at 450 000 rows. Real RCS events do not exist yet (no adapter).
