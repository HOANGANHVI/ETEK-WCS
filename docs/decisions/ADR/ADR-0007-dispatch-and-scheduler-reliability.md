# ADR-0007 – Dispatch and scheduler reliability

Status: accepted (CR-008A, 2026-11-16).

## Context
The code review found that in Production a task could be "accepted" by the RCS simulator and then hang, that a failing station intake stopped the whole scheduler pass, that RCS dispatch / continue / cancel were retried on every 1 s tick (5 attempts = about 5 s, unbounded history growth), and that the cancel reconciliation ignored several RCS statuses.

## Decisions
1. **Production guard.** `IRcsDispatchGuard` refuses task creation (`RCS_NOT_CONNECTED`, 422) and dispatch when the environment is Production and the RCS client is the simulator, unless `Rcs:AllowSimulatorInProduction=true` (supervised test). Development, Testing, Staging and Demo are unchanged. AGV status is not queried from the simulator in that situation (Unknown / `RCS_NOT_CONNECTED`).
2. **Independent tick scopes.** Station intake and scheduler pass use separate try blocks and scopes; intake isolates each request (an unexpected failure is logged and the request is NOT acknowledged; the request key keeps a later attempt idempotent). *Correction (CR-008B-1, 2026-11-17): the original text added "so the PLC presents it again". That is not true of the current code: see the correction section below.*
3. **Spaced retries, per state.** `TransportTask.RcsRetryCount / RcsLastAttemptAt` (reset on every state change) drive `RcsRetryPolicy`: delay = initial x 2^(n-1), capped. This is technical spacing, not a business timeout; `StateTimeoutsSeconds` stays empty. Dispatch keeps `MaxDispatchAttempts` (uncertain failure keeps reservations). Continue and cancel stop their automatic retry after `RcsMaxAttempts`: a one-off history note is written, reconciliation is required and an operator decides - no state is changed by a timer.
4. **History is written when the outcome changes** (`NoteIfChanged`), never once per attempt.
5. **Cancel reconciliation.** Cancelled -> cancelled; Created/Executing -> cancel is asked again (spaced); Cancelling -> wait; Completed / Interrupted -> Failed with reconciliation (rack and reservations kept, AGV freed); NotFound -> only when RCS never reported a task code and two consecutive checks agree (and still with reconciliation if dispatch attempts had been made); NotFound after RCS accepted the task -> Failed with reconciliation; Unknown -> wait.

## Consequences
- With only the simulator, Production cannot create tasks until a real RCS adapter exists (CR for the HIK RCS-2000 integration) - intended.
- An RCS outage now lasts about 62 s (with defaults) before a dispatch is declared uncertain, instead of about 5 s (*correction CR-008B-1, 2026-11-17: originally "about 30 s"*). Continue/cancel retries reach their limit after about 25 minutes.
- Tasks that reach the retry limit, or end as Completed/Interrupted/NotFound-after-accept, need the existing Admin reconciliation.
- Open: real timeout values, AGV Offline policy and RCS status mapping remain NEEDS_CONFIRMATION.

## Corrections (CR-008B-1, 2026-11-17) - history above is kept

1. **Signal edge, WCS acceptance, PLC acknowledgement and request tracking are four different things.**
   - *Signal edge detection:* `PlcStationSignalService.PollRequestsAsync` turns the rising edge of `Pickup_Request` / `Delivery_Request` (per approved station mapping) into a `StationRequest` and remembers the bit as "high" immediately. A request that is still high is NOT returned again.
   - *WCS request acceptance:* a request is accepted by WCS when `CreateTaskAsync` commits (or returns the existing task for the same request key).
   - *PLC acknowledgement:* `AcknowledgeRequestAsync` currently only writes a log line. `Request_Ack` is not written because no handshake rule is approved. The PLC therefore receives no signal from WCS.
   - *Persistent request tracking:* none existed. A request rejected or failed after the edge was detected was lost - acknowledged or not. "Not acknowledged, so the PLC presents it again" was never true. Persistent tracking is introduced by CR-008B-2.
2. **Retry timings with the default options (verified by the policy formula; asserted by a unit test from CR-008B-2):** dispatch attempts at about 0, 2, 6, 14 and 30 s, then a further 32 s wait: declared uncertain at about **62 s**. Continue/cancel: delays 2, 4, 8, 16, 32 s, then 60 s; the 30th attempt is at about 1 502 s (**25 minutes**) and the limit is declared on the next scheduler tick.
3. **NotFound.** Decision 5 above ("NotFound only after two consecutive answers") was a technical proposal that the project owner has NOT approved (D-9) and that conflicts with `task-state-machine.md` 3.9 ("CANCELLED only when RCS confirms the cancellation or an equivalent reconciliation mechanism exists"). It is withdrawn by CR-008B-3: NotFound is never turned into Cancelled.
4. **Retry-limit behaviour.** Decision 3 said the task stays in CancelPending with reconciliation flagged. The owner decided (D-1) that the task becomes Failed with reconciliation required (CR-008B-3).
5. **Station intake (CR-008B-2).** Detected station requests are stored in `station_requests` (unique request key) before processing; `RCS_NOT_CONNECTED` keeps them pending without using an attempt; they are accepted only after the task commit; the PLC-side behaviour (no `Request_Ack`) is unchanged. See `docs/reviews/CR008B-2_PLC_INTAKE_RELIABILITY.md`.
