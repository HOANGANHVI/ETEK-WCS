# ADR-0011 – PLC interface of 20/11/2026: architectural decisions D-1 to D-4 (CR-029)

- **Date:** 2026-11-21
- **Status:** Decisions **accepted by the project owner** (2026-11-21). Implementation: **none yet** (no code, no migration, no database change). The 20/11/2026 interface itself remains **pending PLC engineer confirmation**; CR-029 stays `PENDING_APPROVAL`, not `APPROVED` and not `READY FOR IMPLEMENTATION`.
- **Numbering note:** ADR-0009 and ADR-0010 exist on the unmerged CR-027 / CR-028 branches; this ADR is numbered 0011 to avoid a clash.
- **Supersedes in part:** ADR-0008 decision 4 ("one active Station Transaction per port, no PLC-wide lock") — see D-1. ADR-0004 decisions 6 and 8 and the 23-signal template are affected by the new interface (see CR-029 / Impact Analysis); their text is unchanged.
- **Does not change:** the 13 task states, `PER_AGV` of Legacy dispatch, BP-04 `REVIEW`, WCS ownership of inventory, A3/A4 and D1/D2 `UNCHANGED`, CR-028 decisions, any PLC IP / address / mapping that is running.
- **Inputs:** `AGV_PLC Interface_List_20_11_2026.xlsx`; `docs/02_Interface_Specification/PLC_20_11_2026/*`; conflicts C-1…C-5 of CR-029.

## D-1 – PLC transaction concurrency

**Decision.** At most **one active Handshake transaction per PLC**. Each PLC has its **own FIFO queue**. The four PLCs work **independently and in parallel**. The Stations of one PLC **never perform a Handshake at the same time**.

**Consequences / rules.**
1. Resolves conflict **C-1** in favour of the PLC-level rule (the 2026-11-17 requirement). The "one active transaction per port" rule of ADR-0008 decision 4 is **superseded in part** for the Handshake: a second Station of the same PLC waits in that PLC's queue.
2. **Not implemented.** Checked in the code: there is **no PLC-level FIFO queue** today. `PlcRequestService` *rejects* (`PORT_BUSY`) a request at a port that already has an active Station Transaction and relies on the PLC raising it again; the database only enforces one active Station Transaction **per port** (partial unique index). The existing tests (for example `Cr010MultiLegTests.Test07`, two ports of one PLC active at once) describe the old behaviour. No document may state that PLC-level FIFO is supported until code and tests exist.
3. The FIFO key (order of recognition by WCS, tie-break), the persistence of queued requests, and the queue behaviour after a WCS or PLC restart are **design items for the implementation CR**; the earlier draft proposal (`ReceivedAt`, monotonic sequence per PLC) is a proposal only.
4. **Boundary of "active Handshake" is not defined by any source** and must not be assumed: the current Station Transaction is opened when the leg is dispatched (ADR-0008, O-3) and lasts until the AMR has left the port, which would serialise whole AMR trips if used as the Handshake window. The start and end of the Handshake window (for example from the first handshake signal to the AMR leaving, or to `PLC_MissionCompleted`) must be defined with the PLC engineer (new question PQ-26) before design.
5. The PLC engineer must confirm that this model is compatible with the physical station group (PQ-13, PQ-14, PQ-22 are now confirmation questions, not open choices) and how a waiting Station's request signal behaves while another Station is active (PQ-02, PQ-26).

**Impact to assess in the implementation CR:** ADR-0008; `StationTransaction` and its active-per-port unique index; `StationTransactionService`; `PlcRequestService` (queue instead of reject); `StationRequestProcessor`; `HandshakeCoordinator`; RouteEngine leg dispatch to ports; UI; tests listed in the Test Specification (section 9). A migration and a new queue entity are likely; **none is created now**.

## D-2 – `Mission_Feedback` and the dynamic AMR fleet

**Decision.** `AGV1/AGV2/AGV3` are **not** a fixed identity of an AMR in the WCS–RCS architecture. The RCS manages the dynamic fleet and chooses the AMR. WCS stores the **real `RobotCode`** supplied by the RCS (CR-028: per Task and per Leg). `Mission_Feedback` reflects the **receipt / processing status of the request**; it does **not** by default represent a `RobotCode`.

**Consequences / rules.**
1. Resolves conflict **C-2**.
2. The source workbook is **not edited**: its enum (`0` not yet taken, `1` AGV1, `2` AGV2, `3` AGV3, `>10` error) stays as written. WCS **must not write** `1`–`3` as an AMR identity and must not map a `RobotCode` to AGV1/2/3 on its own.
3. A **new feedback interface is proposed to the PLC side** (proposal IF-P3 in the Vendor Questions): status values for "request received / being processed / AMR assigned / error" separate from any identity. If the PLC needs the real robot identity, a **separate data area** carrying the real `RobotCode` must be proposed and **agreed by both sides** (type, length, encoding; PQ-06, PQ-29). Until then WCS writes nothing to `Mission_Feedback` beyond what is later approved.
4. "Sent ≠ assigned" (CR-028) is kept: no robot identity is ever shown to the PLC before the RCS reports it.

## D-3 – CLAUDE.md section 7

**Decision.** CLAUDE.md section 7 is updated (this task): the 20/11/2026 interface is recorded as the **new interface document, pending confirmation/approval**; the IO Map v2 workbook is the **old baseline**, kept only for reference and historical comparison and **not** the default basis for new design; the 20/11/2026 workbook is **not** an approved mapping and must not be deployed to a real PLC; history and old documents are preserved (nothing deleted).

**Consequences.** Resolves conflict **C-3**. Sections 7.1–7.7 remain as the historical record; the new section 7.8 governs new design work. Station naming (conflict **C-4**, E1/E2/G1/G2 vs G3/G7) is **not decided** by D-1…D-4 and stays open (PQ-20); the configured topology is unchanged.

## D-4 – Physical rack reconciliation

**Decision.** The loss of the ability to reconcile the **physical rack with the WCS inventory is not accepted.** The following must be established (sources and mechanisms to be agreed with the PLC side and the owner):

1. **Presence** of the rack at the Station must be confirmed.
2. The **identity** of the physical rack must be determined **when the process requires it**.
3. A mechanism that gives a request/transaction a **unique identity or prevents duplicate processing** is required (the 20/11 interface has no `Request_ID`; PQ-10).
4. The **safety / interlock conditions** needed before WCS may command anything must be confirmed (the 20/11 interface has none; PQ-18).
5. If the PLC does not provide rack identification data, **another device or system must be identified** that provides it (candidates to be assessed, none chosen: additional PLC signals, a reader/sensor at the Station, the RCS rack code reported for the AMR (`podCode`), operator/scanner confirmation).

**Rule.** `PLC_MissionCompleted = 1` is **necessary but not sufficient** evidence. It is **never** proof that the **correct rack** was handed over or received **at the correct location**. A hand-over is confirmed by WCS only when the rack presence (and identity, when required) is confirmed, the rack is at the right Location, the rack status and position are updated successfully in WCS and the history records it (consistent with CR-028 section 13). Missing or inconsistent evidence stops the flow for reconciliation.

**Consequences.** Resolves conflict **C-5** as a **requirement**, not as an accepted loss: the interface/system must be extended or a data source added. The implementation CR cannot rely on the 20/11 interface alone for rack verification. WCS keeps owning inventory; PLC data never overwrites it.

## Resolution of the CR-029 conflicts

| ID | Resolution |
|---|---|
| C-1 | **Resolved by D-1** (PLC-level single active Handshake + per-PLC FIFO). ADR-0008 decision 4 superseded in part. Not implemented; window boundary open (PQ-26). |
| C-2 | **Resolved by D-2** (real `RobotCode` stored by WCS; `Mission_Feedback` = request status; new interface proposed; no fixed AGV1–3 identity). |
| C-3 | **Resolved by D-3** (CLAUDE.md updated; IO Map v2 = old baseline). |
| C-4 | **Still open** (PQ-20). Not decided by this ADR. |
| C-5 | **Resolved as a requirement by D-4**; the source of rack data is an open item (PQ-27, PQ-28). |

## What is still pending the PLC side

Register addresses; level vs pulse of every signal and reset ownership; value meanings (including error values); one-active-Handshake window and queue behaviour (PQ-26); a unique request/transaction identity (PQ-10); rack presence and identity data (PQ-27, PQ-28); safety/interlock signals (PQ-18); the new feedback interface and any `RobotCode` data area (PQ-06, PQ-29); heartbeat scope; Station naming (PQ-20); timeouts, retries and restart behaviour. See `PLC_VENDOR_CLARIFICATION_QUESTIONS_20_11_2026.md`.

## Not done by this ADR

No code, migration, database, Docker, `.env`, PLC IP or address, and no change to any source workbook.
