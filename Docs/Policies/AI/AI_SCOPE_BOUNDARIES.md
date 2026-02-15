---
document_role: policy
audience: ai, developers
scope: repository
status: active
---

# AI Scope Boundaries

This document defines **what AI is allowed to modify** when working in the Spark Plug repository.

These rules exist to prevent unintended refactors, architectural drift, or large diffs.

AI must treat these as strict limits.

---

## 1. Default Rule

Unless explicitly instructed otherwise:

**Modify only the code required for the current task.**

Avoid:

- Editing unrelated files
- Renaming types or folders
- Reformatting large sections
- “Cleaning up” nearby systems

Target change size:

- Ideal: 1–3 files
- Maximum: 5 files

---

## 2. Changes That Require Explicit Instruction

AI must not perform the following unless the task clearly asks for it.

### 2.1 Architectural changes

Do not:

- Introduce new patterns or layers
- Change dependency direction
- Replace MVVM / Service / CompositionRoot structure
- Introduce service locators, singletons, or frameworks

---

### 2.2 Cross-system refactors

Do not:

- Rename core types
- Move classes between folders/namespaces
- Change public APIs used by other systems
- Modify multiple subsystems for consistency

---

### 2.3 Behavioral changes

Do not alter existing behavior unless required:

- Timing or simulation logic
- Default values
- Initialization order
- Save/load behavior

---

### 2.4 Speculative abstraction

Do not add:

- Generic base classes “for future use”
- Interfaces without multiple implementations
- Config systems not currently required
- “Future-proofing” layers

Spark Plug prefers **concrete implementations**.

---

## 3. Allowed Changes

AI may:

- Implement the requested feature
- Modify related Services, ViewModels, or Views
- Update SaveService or GameData if required
- Add small local improvements:
  - Clearer variable names
  - Null guards
  - Small helper extraction
- Add TODOs when a full solution is out of scope

---

## 4. Persistence Boundary

- Only **SaveService** writes to disk
- Do not change save schema without instruction
- Do not save derived values

---

## 5. Content Boundary

For GameDefinition/content:

Do:

- Validate explicitly
- Fail loudly

Do not:

- Add silent fallbacks
- Invent default values
- Ignore invalid references

---

## 6. When Scope Is Unclear

Choose the smallest safe implementation and stop.

If a change affects multiple systems:
→ wait for confirmation.
