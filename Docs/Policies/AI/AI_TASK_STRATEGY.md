---
document_role: policy
topic: ai
audience: ai, developers
scope: repository
status: active
---

# AI Task Strategy

This document defines how AI should execute implementation work.

## 1. Work in Vertical Slices

Each step should be independently usable:

- Compiles
- Runs
- Preserves existing behavior
- Has a manual test path

## 2. Preferred Implementation Order

1. Data (`Definition`, `Catalog`, validation)
2. Service behavior
3. Persistence for new facts
4. ViewModel adaptation
5. View binding

## 3. Safety Checks Per Step

- No null-risk regressions
- Existing save data still loads
- New fields have safe defaults

## 4. Completion Criteria

A feature slice is complete when:

- Data loads
- Service behavior works
- UI reflects state
- State persists across reload
- No dead code or console errors from the change

## 5. Refactor Posture

Avoid large refactors unless explicitly requested.
Prefer small extractions and local cleanup.

## 6. Unclear Requirements

- Choose smallest safe implementation.
- Add TODOs for deferred behavior.
- Stop for confirmation if scope expands.

For hard edit boundaries, see `AI_SCOPE_BOUNDARIES.md`.
For naming/style constraints, see `AI_STYLE.md`.
