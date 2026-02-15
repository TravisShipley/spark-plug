---
document_role: policy
audience: ai, developers
scope: repository
status: active
---

# AI Scope Boundaries

This document defines what AI is allowed to modify.

## 1. Default Rule

Unless explicitly instructed otherwise, modify only code required for the current task.

Avoid:

- Editing unrelated files
- Renaming types or folders
- Reformatting large sections
- Opportunistic cleanup outside scope

Target change size:

- Ideal: 1-3 files
- Maximum: 5 files

## 2. Changes Requiring Explicit Instruction

Do not do these unless clearly requested:

- Architectural changes
- Cross-system refactors
- Behavior changes (timing/defaults/init/save-load)
- Speculative abstractions

## 3. Allowed Changes

AI may:

- Implement requested features
- Modify related Services/ViewModels/Views
- Update `SaveService`/`GameData` when required by task
- Make small local improvements (naming, null guards, tiny extraction)
- Add TODOs when full work is out of scope

## 4. Persistence Boundary

- Only `SaveService` writes to disk
- Do not change save schema without instruction
- Do not save derived values

## 5. Content Boundary

For GameDefinition/content:

- Validate explicitly
- Fail loudly
- Avoid silent fallbacks/default invention

## 6. When Scope Is Unclear

Choose the smallest safe implementation and stop for confirmation if a change spreads across multiple systems.

For coding style and architectural naming constraints, see `AI_STYLE.md`.
For execution flow (vertical slices), see `AI_TASK_STRATEGY.md`.
