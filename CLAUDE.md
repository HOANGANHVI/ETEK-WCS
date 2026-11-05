# Claude Code – Mandatory Project Rules

Project root: ETEK WCS. Business baseline files are under `docs/` (specification, change-management and decisions). Read `README.md` and `docs/README.md` before proposing implementation. The setup script may create project scaffolding only; do not infer or implement unapproved business workflows.

You are implementing ETEK WCS. The files under `docs/specification` (or this package's `specification/` directory) are the approved business baseline.

## 1. Authority and precedence

1. Explicitly approved business rules and accepted Change Requests.
2. Current approved specification files.
3. Source documents, interpreted only within their stated scope.
4. Your technical inference, which must be labeled as a proposal and must not silently become a business rule.

If sources conflict, do not choose one silently. Record the conflict and request a decision.

## 2. Protected business rules

- Task concurrency is `PER_AGV`: at most one active task per AGV; AGV1 and AGV2 may operate concurrently.
- BP-04 (slide 8: B1/B2 has free position and requires full rack) is `REVIEW`. Do not implement until approved.
- WCS owns business rack inventory and location state.
- A3/A4 and D1/D2 are `UNCHANGED`; do not redesign or replace the legacy process.
- A single PLC connection serves the stations; do not introduce station clusters.
- Do not add bind-rack behavior at points explicitly marked “Không bind rack” in the source process.

## 3. Change-control workflow

When new requirements or documents arrive:

1. Read the source and identify exact new/changed statements.
2. Create a Change Request using `change-management/CHANGE_REQUESTS.md`.
3. Identify conflicts and affected requirements, flows, data, APIs, PLC signals, RCS integration, UI, backend and tests.
4. Produce an impact report.
5. Stop before modifying approved specification or source code until the project owner approves the Change Request.
6. After approval, update only affected files.
7. Update CHANGELOG, Change Request status, Impact Matrix and Traceability Matrix.
8. Run consistency checks and relevant regression tests.

Do not treat “please analyze this document” as permission to implement it.

## 4. Implementation safety

- Do not rewrite whole modules for a narrowly scoped change.
- Do not refactor unrelated code during a requirement change unless separately approved.
- Preserve existing APIs and database behavior unless the Change Request explicitly authorizes a breaking change.
- Use database migrations; never silently drop or rename production data.
- Use feature flags or configuration for incomplete/optional behavior where appropriate.
- Keep PLC and RCS vendor adapters isolated from business logic.
- Do not hard-code IPs, ports, credentials, PLC addresses, RCS URLs, robot codes, workstation codes or task template codes.
- Make task creation, callbacks, inventory updates and retries idempotent.
- Protect AGV, Rack and Location resources against concurrent allocation.
- Never mark a rack at its destination merely because a task was created or dispatched.

## 5. Required response before coding

For each approved change, first provide:
- Change ID and approved scope.
- Files/modules expected to change.
- Database migration impact.
- API and integration impact.
- Regression tests to run.
- Risks and rollback approach.

If implementation reveals an unapproved business ambiguity, stop and ask. Do not invent a resolution.

## 6. Completion report

After implementation, report:
- Files changed.
- Behavior added/changed.
- Tests run and results.
- Database migration details.
- Remaining risks/TBD items.
- Confirmation that protected rules and unrelated flows were not changed.
