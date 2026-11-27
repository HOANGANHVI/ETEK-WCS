# CR-032 – UI walkthrough in a real browser (2026-11-23)

**What was run.** Google Chrome (headless, driven by Playwright), the Vite frontend, the real backend (`Development`, `Rcs:Fleet:Source=Real`, fleet interval 3600 s so the monitoring poll does not mix with the counts) on a **throw-away PostgreSQL database** (`wcs_cr032_ui`, not the development database), and a **mock RCS** (a small local HTTP server on `127.0.0.1:18994` that records every request it receives and answers: code 0 by default, `cancelTask` → HTTP 200 + code 1, `getOutPod` → HTTP 500, `returnPod` → HTTP 200 with an HTML body, `queryTaskStatus` → a response containing a `token` field). Nothing here touched a real RCS or AMR.

**Result: 43 / 43 checks passed** (the script is not in the repository; it lives in the session scratchpad). Screenshots were reviewed for the Critical warning screen, the page, the inspector and the history.

| Area | Verified in the browser |
|---|---|
| Open the page | `/rcs-diagnostics` as Admin loads the catalog; **0 requests reached the mock** on open. |
| Catalog | 23 API entries, names equal to the backend catalog. |
| Every API (23/23) | Selecting it shows the Required / Optional badges, the risk class equals the backend catalog, the "Fill the document sample" button produces a body that the server accepts (Execute enabled). **Selecting all 23 and filling their samples sent 0 requests.** |
| Validation | `cancelTask` empty → Execute disabled and the rule message shown; invalid JSON in the JSON editor → flagged and Execute disabled; valid JSON → enabled. |
| Execute | One click sent **exactly one request** to the mock at once; **no confirmation modal appeared** (polled every 20 ms) and there is no approval or pending state. The body carried the typed fields plus generated `reqCode` / `reqTime`. |
| Inspector | HTTP 200 + RCS code 1 is shown as "HTTP ok but RCS reports failure" (not success); success shows HTTP 200 and the data; HTTP 500 shown as HTTP failure; HTTP 200 with an HTML body shown as JSON failure. |
| Secrets | A typed `tokenCode` reached the mock (the Admin chose it) but appears nowhere in the page; the `token` returned by the mock is masked (`***`); neither is in the history, the export, or the audit log (queried through the API). |
| Dangerous APIs | The risk class and a consequence line appear above the Execute button for `stopRobot`, `clearRoadWay`, `cancelTask`, `blockArea`, `lockPosition`, `bindPodAndBerth`, `bindCtnrAndBin`, `getOutPod`, `returnPod`, `boxApplyPass`, `syncMapDatas` (Unverified) and `queryAgvStatus` (Read-only, no extra line). `stopRobot` with `robotCount = -1` adds the "ALL AMRs" warning. No second step. |
| History / export | The history gained exactly the 5 executions; detail opens the Inspector; the failure filter returned the 3 failed calls; the JSON export downloaded and contained no secret. |
| Callback monitor | 5 callbacks listed as "not provided in this build"; no received entries. |
| Overview checks | "Check TCP" sent no HTTP request; "Check API" sent exactly one `queryAgvStatus`. |
| Operator | Signed in as Operator: no menu entry; opening `/rcs-diagnostics` is redirected to the home page; the catalog, execute and tcp-check endpoints answer **403** and nothing reaches the mock. |
| Console | No page error and no console error (network 401 / 400 messages of the setup steps excluded). |
| Fleet monitoring | After ~40 manual calls the fleet state's last attempt still shows only the start-up poll: manual calls do not touch it. |

**Findings during the walkthrough.** None in the application. Four defects were in the walkthrough script itself (a button selector that matched the wrong button, a forced first-login password change of the test Operator, history counts accumulating across reruns, expected network-error console noise) and were fixed in the script. One wording duplication in the Critical notice ("Nguy hiểm. NGUY HIỂM: …") was found in the screenshot and fixed.

**Not covered.** A real RCS-2000; narrow / mobile layouts; English UI visually (the i18n key check covers completeness).
