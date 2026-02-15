---
document_role: policy
audience: ai, developers
scope: repository
status: active
---

# AI Policy Index

This folder defines AI behavior. Each file has a single purpose.

## Read Order

1. `AI_SCOPE_BOUNDARIES.md`
2. `AI_STYLE.md`
3. `AI_TASK_STRATEGY.md`

## Ownership

- `AI_SCOPE_BOUNDARIES.md`
  - What may or may not be changed.
  - Scope limits, refactor limits, persistence/content boundaries.
- `AI_STYLE.md`
  - How code should look when implementing approved changes.
  - Naming, layer responsibilities, editing hygiene.
- `AI_TASK_STRATEGY.md`
  - How to execute work.
  - Vertical-slice sequencing, validation, completion criteria.

Do not duplicate rules across these files; link to the owner document instead.
