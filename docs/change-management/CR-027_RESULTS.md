# CR-027 Phase 1 – Results

Run on branch `feature/cr-027-dynamic-amr-monitoring` (2026-11-19). Baseline: `CR-027_BASELINE.md`.

## 1. Tests

| Check | Baseline (main a0c4c70) | After Phase 1 |
|---|---|---|
| Backend tests | 869 / 869 (+1 golden test added before any code change = 870) | **901 / 901** (870 + 31 new in `Cr027FleetTests`; the full run was repeated: one earlier full run had a single failure in `Cr026ConfigTransferTests.Conflicts_must_be_excluded…`, which passed 4 times alone and in the next full run – an isolated timing flake of a CR-026 test under load, not related to CR-027; reported, not hidden) |
| `tsc -b`, ESLint, `npm run i18n:check`, `npm run build` | clean | clean |
| Frontend smoke suites | i18n 76, dashboard 18, preferences 10, monitor 23, plcwrite 20, plcmonitor 29 | same counts, all passed |

## 2. Before / after proof for transport statistics

`Cr027StatisticsBaselineTests.Golden` was captured on unchanged code and committed before the first CR-027 source change (commit `4f39a4c`). It covers tasks per state, completed tasks per AGV, task history events, orders, legs (trips), station transactions, inventory transactions, racks, reservations and the AGV configuration rows. After CR-027 the **same text** is produced while the fleet is polled all the way through the scenario with **0, 1, 2, 3 and 40/0 alternating AMRs** (`The_transport_statistics_are_exactly_the_baseline_whatever_the_number_of_AMRs_the_RCS_reports`, 5 cases) – identical. Dashboard / KPI code (`lib/dashboardStats.ts`, `DashboardPage.tsx`) is unchanged (sha256 as recorded in the baseline).

## 3. Schema proof

`node schema.mjs` fingerprints of the EF snapshot blocks for `agvs, transport_tasks, task_history, transport_orders, transport_legs, station_transactions, reservations, racks, locations, inventory_transactions, station_requests`: **identical before and after** (`diff` empty). The only schema change is the new migration.

## 4. Migration

`20261119…_AddRcsFleetSnapshots` – creates `rcs_amr_snapshots` and `rcs_fleet_state`; additive; not applied to any deployed database. Rollback: see CR-027 section 4.

## 5. Not verified with a real RCS

See `CR-027_DYNAMIC_AMR_MONITORING.md` section 7. The real HTTP client was exercised only against a local test server that answers like the API document (request path and body, success, HTTP 500, timeout, `code` ≠ 0, invalid body, refused connection, no saved connection).
