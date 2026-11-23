# CR-032 – RCS Diagnostics & Manual API Tester (Admin only)

| | |
|---|---|
| **Status** | Implemented on the feature branch, **MOCK verified**. **Not verified against a real RCS-2000** (the build machine cannot reach it) and **no command API was ever sent to a real AMR**. Not merged. |
| **Branch** | `feature/cr-032-rcs-diagnostics` (from `main` `047ef05`). |
| **Source** | `docs/03_RCS_Interface/UD35865B_RCS-2000 API_Developer Guide_V3.3_20231204.pdf`: chapters 2, 3, 5.1, 5.2 (5.2.1 – 5.2.23), 5.3, Appendix A. |
| **Scope** | A diagnostic tool. It does not change Real Dispatch, the 5 s fleet monitoring, PLC handshake, inventory / rack logic or Legacy dispatch, and nothing it does is operational state. |

## 1. What it is

An independent module (menu **System → Chẩn đoán RCS / RCS Diagnostics**, route `/rcs-diagnostics`) with five areas:

1. **RCS Connection Overview** – the *saved* RCS connection (no separate connection), source (always the real RCS), last TCP check and last API check kept apart (a TCP success is never an API success).
2. **API Catalog** – the 23 outbound APIs by group, search / group / adapter filter, method, endpoint, purpose, impact, permission, documented gaps.
3. **Request Builder** – form generated from the document's field table (required / optional, type, maximum length, enumeration, nested lists) or a JSON editor; `reqCode` / `reqTime` generated when empty; server-side validation and preview of exactly what will be sent.
4. **Request / Response Inspector** – method, endpoint, headers (no credential exists in the call), masked body, HTTP status, safe response headers, masked response, RCS code / message, DurationMs, request id, outcome class.
5. **Execution History** – every call, with filters (time, API, user, status/outcome), detail view, JSON export, "copy for the RCS engineer".

Plus a **Callback Monitor** (see 4).

## 2. Rules implemented

| Rule | Implementation |
|---|---|
| Admin only, enforced at the backend | `RequireAuthorization(AdminPolicy)` on the whole `/api/rcs-diagnostics` group **and** `RcsDiagnosticsService.RequireAdmin` on every method. Operator → 403, anonymous → 401 (the build has two roles, Admin and Operator; there is no Viewer role). |
| No approval, no second Admin, no pending state, no confirmation dialog | `POST /execute` sends immediately. The impact text is shown *before* the button; the button sends at once. |
| Validate before sending | `RcsRequestValidator` (required, type, max length, enum, numeric range, array size, document rules: at-least-one / exactly-one / required-when). An invalid request is **not sent** (`RCS_DIAG_VALIDATION_FAILED`) and is audited as Failed. Fields the document does not list only warn. |
| No arbitrary URL (SSRF) | The path comes from `RcsApiCatalog` only; host, port and scheme come from the saved RCS connection. Unknown or hand-made API names → `RCS_DIAG_API_NOT_IN_CATALOG`; `host` / `port` / `url` / `baseUrl` / `endpoint` / `address` / `uri` / `path` / `scheme` in the body → refused. |
| Success is never "HTTP 200" | Outcome classes: `API_SUCCESS` (HTTP 2xx + JSON + `code` = "0"), `TCP_CONNECTION_FAILED`, `TIMEOUT`, `HTTP_FAILURE`, `RCS_CODE_NOT_SUCCESS` (HTTP ok, code ≠ 0), `JSON_PARSE_FAILED`, `RESPONSE_INVALID` (JSON without `code`), `NOT_CONFIGURED`. |
| Masking | Keys matching token / password / secret / authorization / credential are replaced by `***` in the request, response, history, export and audit; credential-like text in messages is masked. The real `tokenCode` typed by the Admin is sent to the RCS but never returned or stored. |
| Audit | `RCS_DIAG_EXECUTE` (who, when, API, `reqCode`, outcome, HTTP, RCS code, duration, endpoint; Success / Failed), `RCS_DIAG_TCP_CHECK`, `RCS_DIAG_EXPORT`. |
| Does not disturb the 5 s polling | Own `HttpClient` and gate (`Rcs:Diagnostics:MaxConcurrent`, default 2; a surplus request is refused with `RCS_DIAG_BUSY`, never queued). It never touches `FleetService`, fleet snapshots or state. |
| Diagnostics ≠ operations | Opening the page, the catalog, the overview, the history, a preview or selecting an API sends nothing. Only Execute, *Check TCP* and *Check API* call the RCS. No write to inventory, racks or transport tasks. |
| No simulator | The module never uses `IRcsClient` / the simulator, whatever `Rcs:Fleet:Source` says. With no saved connection it answers `NOT_CONFIGURED` and sends nothing. |
| Timeout | The saved connection's timeout (clamped 1–60 s, default 10 s). |

## 3. The 23 outbound APIs (document V3.3, chapter 5.2)

Every one has a documented schema and an adapter (`Executable = true`). "Effect": **R** = read-only, **D** = writes RCS data / configuration, **A** = creates, changes or stops AMR work.

| Group | API | § | Endpoint (POST) | Effect |
|---|---|---|---|---|
| Query | queryAgvStatus | 5.2.14 | `/rcms-dps/rest/queryAgvStatus` | R |
| Query | queryTaskStatus | 5.2.15 | `/rcms/services/rest/hikRpcService/queryTaskStatus` | R |
| Query | queryPodBerthAndMat | 5.2.16 | `…/hikRpcService/queryPodBerthAndMat` | R |
| Task | genAgvSchedulingTask | 5.2.1 | `…/hikRpcService/genAgvSchedulingTask` | A |
| Task | continueTask | 5.2.2 | `…/hikRpcService/continueTask` | A |
| Task | cancelTask | 5.2.3 | `…/hikRpcService/cancelTask` | A |
| Task | setTaskPriority | 5.2.4 | `…/hikRpcService/setTaskPriority` | A |
| Task | genPreScheduleTask | 5.2.7 | `…/hikRpcService/genPreScheduleTask` | A |
| Task | genCtuGroupTaskBatch | 5.2.21 | `…/hikRpcService/genCtuGroupTaskBatch` | A |
| AMR control | stopRobot | 5.2.10 | `…/hikRpcService/stopRobot` | A |
| AMR control | resumeRobot | 5.2.13 | `…/hikRpcService/resumeRobot` | A |
| Location / map / area | lockPosition | 5.2.9 | `…/hikRpcService/lockPosition` | D |
| Location / map / area | syncMapDatas | 5.2.11 | `…/hikRpcService/syncMapDatas` | R |
| Location / map / area | blockArea | 5.2.12 | `…/hikRpcService/blockArea` | A |
| Location / map / area | clearRoadWay | 5.2.8 | `…/hikRpcService/clearRoadWay` | D |
| Location / map / area | blockStgBin | 5.2.18 | `…/hikRpcService/blockStgBin` | D |
| Binding / material / rack | bindPodAndBerth | 5.2.5 | `…/hikRpcService/bindPodAndBerth` | D |
| Binding / material / rack | bindCtnrAndBin | 5.2.6 | `…/hikRpcService/bindCtnrAndBin` | D |
| Binding / material / rack | bindPodAndMat | 5.2.17 | `…/hikRpcService/bindPodAndMat` | D |
| Binding / material / rack | bindMaterialTypCodeAndBerth | 5.2.23 | `…/hikRpcService/bindMaterialTypCodeAndBerth` | D |
| CTU | getOutPod | 5.2.19 | `/rcms/services/rest/hikTpsService/getOutPod` | A |
| CTU | returnPod | 5.2.20 | `…/hikTpsService/returnPod` | A |
| CTU | boxApplyPass | 5.2.22 | `…/hikRpcService/boxApplyPass` | A |

(`…` = `/rcms/services/rest`.)

## 4. Callback Monitor

`agvCallback`, `warnCallback`, `bindNotify`, `applyReturnForValid`, `applyReturnForBin` (5.3) are made by the RCS **to** a third-party platform. They are not outbound APIs and are **not** in the catalog. On this build WCS provides **no** receiving endpoint (the `agvCallback` receiver exists only on the unmerged CR-030 branch), so the monitor lists the five with status *Not provided in this build*, and shows the table `rcs_callback_log` (empty). Nothing is fabricated; a future receiver only has to insert rows.

## 5. Files

Backend: `WCS.Domain/RcsDiagnostics/RcsDiagnostics.cs` (entities), `WCS.Application/RcsDiagnostics/{RcsApiCatalog,RcsRequestValidator,RcsDiagnosticsService}.cs`, `WCS.Infrastructure/RcsDiagnostics/RcsDiagnosticsInfrastructure.cs` (transport, repository), `WCS.Api/Api/RcsDiagnosticsEndpoints.cs`, DI / DbContext / entity configuration, migration `20261123031448_AddRcsDiagnostics`.
Frontend: `pages/RcsDiagnosticsPage.tsx`, `pages/rcsdiag/*`, `api/rcsDiagTypes.ts`, `api/client.ts` (`rcsDiag*`), `i18n/messages/rcsDiag.ts`, `styles/rcsdiag.css`, route, menu entry (Admin, hidden in Demo mode), error / audit translations, `scripts/check-i18n.mjs` (dynamic key prefixes).

API: `GET overview | catalog | history | history/{id} | history/export | callbacks`, `POST preview | execute | tcp-check | api-check` under `/api/rcs-diagnostics` (all Admin-only).

## 6. Database

One additive migration, `AddRcsDiagnostics`: creates `rcs_api_tests` (masked history) and `rcs_callback_log`. No existing table or column is changed or dropped. **Rollback:** `dotnet ef database update 20261121033125_AddLegRobotCode` (drops the two new tables) and remove the code; nothing operational depends on them. History is trimmed to the newest `Rcs:Diagnostics:HistoryKeep` (5000) rows.

Configuration (all optional): `Rcs:Diagnostics:MaxConcurrent` (2), `DefaultTimeoutSeconds` (10), `MaxResponseBytes` (65536), `HistoryKeep` (5000).

## 7. Tests (MOCK RCS: a local server that records what it receives)

`Cr032RcsDiagnosticsTests` – 78 cases: catalog is exactly the 23 APIs and none of the 5 callbacks, documented paths; the sample of each of the 23 APIs validates and each required field / maximum length / enumeration is enforced (per API); document rules; **Admin only** on every endpoint (Operator 403, anonymous 401, nothing sent) and in the service; execute sends immediately with the documented body; **each of the 23 APIs reaches the mock by POST on its documented path with its body**; invalid request not sent and audited; HTTP 200 + code ≠ 0 is a failure; HTTP 500 / 404 / 401, invalid JSON, empty body, JSON without code, array; timeout; connection refused; TCP success ≠ API success; overview calls nothing and shows no secret; history, audit and filters, export; masking everywhere; no change to fleet snapshots / tasks / racks, also with `Rcs:Fleet:Source=Simulator`; no saved connection → `NOT_CONFIGURED`, nothing sent, no simulator answer; opening the module sends nothing; no arbitrary API name / URL / host / port; concurrency limit (409 `RCS_DIAG_BUSY`); a slow manual call does not block a fleet refresh and does not write its snapshot; callback monitor honest. Frontend: `tsc -b`, ESLint, `i18n:check`, `build` – clean.

## 8. Not verified / open points

- **No real RCS observed.** Every request / response shape is the document's; real answers may differ.
- **Command APIs** (the 12 *A* and 7 *D* APIs) were exercised only against the mock. Using them on a real RCS moves, stops or reconfigures real equipment immediately; the owner decided there is no approval step.
- The browser UI was verified by type-check, lint, i18n check and build, **not** by a visual run against a real backend.
- Unclear points of the V3.3 document are listed per API in the catalog (`gaps`) and summarised in the completion report (e.g. `queryAgvStatus` sample uses `mapCode` while the table says `mapShortName`; `blockStgBin` sample key `stgBunCode`; `blockArea` sample omits the required `noticeThird`; Required / Optional flags of `returnPod` read from column order; `getOutPod` / `returnPod` use `hikTpsService`).
- A history row stores the masked request and response but not the HTTP response headers (they are shown only for the live result).
