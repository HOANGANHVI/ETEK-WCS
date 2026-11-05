# Change Request Register

## Status definitions

- `DRAFT`: request captured but not analyzed.
- `ANALYSIS`: impact and conflicts being assessed.
- `PENDING_APPROVAL`: proposal ready for project owner decision.
- `APPROVED`: authorized for specification/code change.
- `REJECTED`: not accepted.
- `IMPLEMENTED`: change implemented and verified.
- `CLOSED`: evidence and release documentation complete.

## Rules

- Every change to approved behavior requires a CR ID.
- A new source document is evidence/input, not automatic approval.
- Keep original source files immutable and record filename/version/date.
- One CR should represent one coherent scope.
- If a request changes multiple independent behaviors, split it into multiple CRs.
- Do not close a CR until specification, code (if applicable), tests and changelog are updated.

## Register

| CR ID | Title | Source | Status | Target version | Decision |
|---|---|---|---|---|---|
| CR-000 | Establish v1.3 change-management framework | Project owner confirmation | APPROVED | 1.3 | Framework only; no business behavior change |

## New Change Request template

### CR-XXX – <title>

- **Status:** DRAFT
- **Requested by:**
- **Date:**
- **Source document:** filename, revision, date, page/slide/section
- **Current baseline:** vX.Y.Z
- **Problem / business reason:**
- **Requested behavior:**
- **Current behavior:**
- **Proposed behavior:**
- **Conflicts with existing rules:**
- **Affected specification files:**
- **Affected software components:**
- **Database impact:**
- **PLC / RCS / external integration impact:**
- **Security / safety / operational impact:**
- **Acceptance criteria:**
- **Regression tests:**
- **Rollback approach:**
- **Open questions:**
- **Decision:** PENDING
- **Approved by / date:**

## Example – do not treat as approved

### CR-EXAMPLE – Add buffer F6

- **Status:** EXAMPLE ONLY – NOT A REAL REQUEST
- **Requested behavior:** Add a new full-rack buffer location F6.
- **Potential impact:** process flows, inventory model, location configuration, UI and tests.
- **Decision:** Not approved. This example must not be implemented.
