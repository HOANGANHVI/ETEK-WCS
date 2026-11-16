# ADR-0007 – Dispatch and scheduler reliability

Status: accepted (CR-008A, 2026-11-16).

## Context
The code review found that in Production a task could be "accepted" by the RCS simulator and then hang, that a failing station intake stopped the whole scheduler pass, that RCS dispatch / continue / cancel were retried on every 1 s tick (5 attempts = about 5 s, unbounded history growth), and that the cancel reconciliation ignored several RCS statuses.

## Decisions
1. **Production guard.** `IRcsDispatchGuard` refuses task creation (`RCS_NOT_CONNECTED`, 422) and dispatch when the environment is Production and the RCS client is the simulator, unless `Rcs:AllowSimulatorInProduction=true` (supervised test). Development, Testing, Staging and Demo are unchanged. AGV status is not queried from the simulator in that situation (Unknown / `RCS_NOT_CONNECTED`).
2. **Independent tick scopes.** Station intake and scheduler pass use separate try blocks and scopes; intake isolates each request (an unexpected failure is logged and the request is NOT acknowledged so the PLC presents it again; the request key keeps the retry idempotent).
3. **Spaced retries, per state.** `TransportTask.RcsRetryCount / RcsLastAttemptAt` (reset on every state change) drive `RcsRetryPolicy`: delay = initial x 2^(n-1), capped. This is technical spacing, not a business timeout; `StateTimeoutsSeconds` stays empty. Dispatch keeps `MaxDispatchAttempts` (uncertain failure keeps reservations). Continue and cancel stop their automatic retry after `RcsMaxAttempts`: a one-off history note is written, reconciliation is required and an operator decides - no state is changed by a timer.
4. **History is written when the outcome changes** (`NoteIfChanged`), never once per attempt.
5. **Cancel reconciliation.** Cancelled -> cancelled; Created/Executing -> cancel is asked again (spaced); Cancelling -> wait; Completed / Interrupted -> Failed with reconciliation (rack and reservations kept, AGV freed); NotFound -> only when RCS never reported a task code and two consecutive checks agree (and still with reconciliation if dispatch attempts had been made); NotFound after RCS accepted the task -> Failed with reconciliation; Unknown -> wait.

## Consequences
- With only the simulator, Production cannot create tasks until a real RCS adapter exists (CR for the HIK RCS-2000 integration) - intended.
- An RCS outage now lasts about 30 s (with defaults) before a dispatch is declared uncertain, instead of about 5 s.
- Tasks that reach the retry limit, or end as Completed/Interrupted/NotFound-after-accept, need the existing Admin reconciliation.
- Open: real timeout values, AGV Offline policy and RCS status mapping remain NEEDS_CONFIRMATION.
