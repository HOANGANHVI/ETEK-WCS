# ADR-0001: Specification Baseline and Change Control

- **Status:** Accepted
- **Date:** 2026-10-31
- **Decision owners:** Project owner

## Context

The WCS project will receive additional process documents and integration details over time. Rewriting the full specification for each new document risks unintended changes to approved workflows and implementation.

## Decision

- Maintain a versioned approved specification baseline.
- Require a Change Request for changes to approved behavior.
- Perform impact analysis before editing specification or code.
- Update only affected files after approval.
- Maintain changelog, CR register, impact matrix and traceability.
- Preserve REVIEW, TBD and UNCHANGED statuses.
- Use Git commits to track each approved change.

## Consequences

- Changes are auditable and reversible.
- Unrelated flows are protected.
- More discipline is required before implementation.
- New source documents do not automatically override approved requirements.
