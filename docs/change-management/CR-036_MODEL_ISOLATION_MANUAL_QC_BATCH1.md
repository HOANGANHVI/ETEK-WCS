# CR-036 – Product Model isolation and Manual QC protection (Batch 1)

| | |
|---|---|
| **Status** | `IMPLEMENTED (BATCH 1) – TESTED – NOT DEPLOYED`. Feature flag `ModelIsolation:Mode` defaults to `Off` (behaviour unchanged) |
| **Source** | Project owner instruction (2026-11-25, "TASK P2-A – CODING BATCH 1") based on `docs/reports/phase2/P2A_MODEL_ISOLATION_MANUAL_QC_CR_DESIGN.md` and `P2A_IMPLEMENTATION_CHECKLIST.md` |
| **Branch** | `feature/ph2-first-runnable` (CR-030 merge still uncommitted, not completed) |
| **Technical decisions used for the code (NOT recorded as closed OQs)** | P2-OQ-03 = table `product_models`; P2-OQ-04 = option A (model declared at stock-in, existing racks assigned by Admin with audit, never inferred); P2-OQ-08 = `Rack.Disposition`; Re-intake NOT implemented |

## Implemented
- **Schema (additive, migration `AddProductModelIsolation`):** table `product_models`; `locations.ProductModelId`, `locations.StationRole`; `racks.ProductModelId`, `racks.Disposition` (default `Auto`); `business_processes.ProductModelId`. Existing rows: model/role NULL, disposition `Auto`. No UPDATE of data. `Down` raises `ROLLBACK_REFUSED` while any rack is in manual handling or any model data exists.
- **Domain:** `ProductModel`, `StationRole`, `RackDisposition`; `Rack.MarkManualHandling()` is one way (no method sets `Auto`); `Rack.AssignModel` never changes an existing model; `ProcessSnapshot.ProductModelId` (nullable, old snapshots read as null).
- **Guards (`ModelIsolation:Mode` Off / Warn / Enforce):** INV-M1/M4/M6 at publication (`PROCESS_MODEL_MISMATCH`, `MANUAL_QC_NOT_BUFFER`, `MANUAL_QC_NOT_SOURCE`, `LEGACY_PROCESS_MODEL_LOCATION`; warnings in Warn); INV-M2/M3 at run time in the condition evaluator, `CreateLegAsync` (including `PreviousDestination`) and the PLC pickup check (`RACK_MODEL_UNKNOWN`, `MODEL_MISMATCH`, `RACK_MANUAL_HANDLING`); events `MODEL_GUARD` (rate limited).
- **Manual QC:** `RackPlacement` is the single place that sets `ManualHandling` when a rack is placed at a `StationRole=ManualQcStation` location (route drop, legacy drop, reconciliation, new stock) in the same unit of work with an `InventoryTransaction(DispositionChanged)`. `LocationType.ManualBuffer` is NOT reinterpreted (F5 unchanged). A physical drop with a model mismatch is never refused: the destination is flagged `ReconciliationRequired` and `MODEL_MISMATCH_AT_HANDOVER` is written.
- **Repository:** new overload `IRackRepository.FindUnreservedAsync(..., RackSelection)`; the original overloads (legacy) are unchanged.
- **API (Admin, audited):** `GET/POST/PUT /api/product-models`, `POST /api/inventory/racks/{id}/model`; optional `productModelId` on initial-stock / adjustments / process header; optional `productModelId`, `stationRole` on location config (null = unchanged). DTOs gain trailing optional fields.

## Not implemented (by instruction or open)
Re-intake / any way back to `Auto`; PH2-05/06 and `reference-processes.json` unchanged; UI (ConfigTransfer export/import of the model fields and the model check on reconciliation were added in A.1 below); Queue, Scope, FIFO; PLC runtime / IO mapping / RCS dispatch untouched.

## Open questions (unchanged)
P2-OQ-01, 02, 05, 06, 07, 11, 12, 16 (stock adjustment of manual-handling racks: refused for now), 17, 18 and all P0 OQs.

## A.1 – Stabilization and safety gaps (2026-11-25)
- **ConfigTransfer:** new group `ProductModel` (`product-models.json`, keyed by code); Location records carry optional `productModelCode` / `stationRole`, process records optional `productModelCode` (written only when set, by code never by id). The new group is applied before Location and BusinessProcess through a soft ordering (`ApplyAfter`), so no existing export gets a new dependency. An import only acts on a field that is PRESENT in the package: an old package never clears a model, a role or a process model. Conflicts: `LOCATION_HAS_RACKS`, `LOCATION_REFERENCED_BY_PUBLISHED` (also enforced by the API path), `PROCESS_MODEL_LOCKED`, `MISSING_DEPENDENCY`.
- **Reconciliation of a legacy task:** the confirmed physical position is always recorded; the rack model is never changed; with `Warn` a `MODEL_GUARD` history entry and a log line are written, with `Enforce` the location is also flagged `ReconciliationRequired` (so nothing is dispatched from or to it automatically). `Off` = unchanged. The legacy source search no longer reserves a rack in manual handling when enforcing.
- **Stock adjustment:** a rack in manual handling is never removed like an Auto rack; the refusal is `RACK_MANUAL_HANDLING_REQUIRES_APPROVED_ADJUSTMENT` (a real reservation keeps `RACKS_RESERVED`). P2-OQ-16 stays open (fail closed).
- **Test stabilization:** the intermittent failure of `Cr031…The_RCS_is_lost_and_comes_back…` was a test that stopped exactly at the 15 s staleness limit, where the answer depends on the sub-microsecond ticks of the starting clock reading after the PostgreSQL round trip; the production rule (strict ">") is unchanged and pinned by a deterministic boundary test. The scratch-database cleanup timeout of the fixture was raised (180 s).
