# ETEK WCS – Specification Package

**Version:** 1.3  
**Status:** Change-management-ready baseline  
**Baseline inherited from:** v1.2

## Purpose

This package defines the approved business baseline and the controlled process for evolving the ETEK WCS specification without unintentionally changing unrelated behavior.

## Directory structure

- `specification/`: current business and integration specification.
- `change-management/CHANGELOG.md`: released specification versions.
- `change-management/CHANGE_REQUESTS.md`: change request register and template.
- `change-management/IMPACT_MATRIX.md`: requirement-to-component impact matrix.
- `change-management/REQUIREMENT_TRACEABILITY.md`: traceability from requirements to flows, components and tests.
- `CLAUDE.md`: mandatory working rules for Claude Code.
- `decisions/ADR/`: architecture decision records.

## Approved baseline rules carried forward

1. Task concurrency: `PER_AGV`.
2. Slide 8 / B1-B2 full-rack delivery flow: `REVIEW`, not approved for implementation.
3. Rack inventory / bind-rack business ownership: `WCS`.
4. A3/A4 and D1/D2: `UNCHANGED`, preserve the legacy process.

## Change-control rule

Never overwrite the approved baseline just because a new source document is received. First create a Change Request, analyze conflicts and impacts, obtain approval, then update only affected specification files and tests.

## Versioning

- **Major**: incompatible business or architecture baseline change.
- **Minor**: approved additive capability or process change.
- **Patch**: clarification or correction that does not change approved behavior.

The project owner approves changes to business behavior. Technical implementation proposals do not by themselves authorize a business rule change.

## Important source limitations

This package does not invent missing details. In particular, BP-04 remains REVIEW; legacy A3/A4 and D1/D2 process details require the original process document; PLC addresses, RCS production configuration, workstation codes and task templates require confirmation.
