````

---
document_role: policy
audience: ai, developers
scope: repository
status: active
---

# AI Scope Boundaries

This document defines **what AI is allowed and not allowed to change** when working in the Spark Plug repository.

The goal is to prevent unintended architectural drift, large refactors, or speculative changes.

AI must treat these boundaries as strict constraints.

---

# 1. Default Scope Rule

Unless explicitly instructed otherwise:

**AI may only modify code directly related to the current task.**

Avoid:
- Editing unrelated files
- Renaming systems
- Moving architecture layers
- Reformatting large sections
- “Cleaning up” nearby code

---

# 2. Forbidden Changes (Without Explicit Instruction)

AI must NOT:

## 2.1 Architectural Changes

Do not:

- Introduce new architectural patterns
- Change dependency direction
- Replace MVVM / Services / CompositionRoot patterns
- Add service locators or global singletons
- Introduce new frameworks or libraries

---

## 2.2 Cross-System Refactors

Do not:

- Rename core types (`GeneratorService`, `NodeCatalog`, etc.)
- Move classes between folders/namespaces
- Change public APIs used by other systems
- Modify multiple subsystems for consistency

If consistency improvements are desired, they will be requested explicitly.

---

## 2.3 Speculative Abstractions

Do not add:

- Generic base classes “for future reuse”
- Interfaces without multiple implementations
- Configuration systems that are not currently needed
- “Future-proofing” layers

Spark Plug prefers **concrete, minimal implementations**.

---

## 2.4 Behavioral Changes

Do not change existing behavior unless the task explicitly requires it.

Avoid:
- Altering timing
- Changing default values
- Modifying save logic
- Changing initialization order

If behavior must change, it should be stated in the task.

---

## 2.5 Style-Only Changes

Do not perform:

- Large formatting changes
- File-wide renames
- Style rewrites
- Reordering members
- Converting patterns (e.g., LINQ ↔ loops)

Small local clarity improvements are acceptable.

---

# 3. Allowed Changes

AI may:

### 3.1 Implement the Requested Feature

- Modify relevant services
- Update ViewModels
- Update Views
- Update GameData / SaveService if required

---

### 3.2 Make Small Local Improvements

Allowed when directly adjacent to the change:

- Rename a local variable for clarity
- Extract a small helper method
- Remove dead code in the same block
- Add null checks or guards

---

### 3.3 Add TODOs for Future Work

If a full solution would exceed scope:

```csharp
// TODO: Support additional modifier operations if required by content
```

Do not implement speculative systems.

---

# 4. File Change Limits

**Ideal change size**
- 1–3 files

**Maximum**
- 5 files

If more changes are required:
→ break the work into multiple steps.

---

# 5. Dependency Direction (Must Not Be Violated)

Always maintain:

```
Definition
    ↓
Catalog
    ↓
Service
    ↓
ViewModel
    ↓
View
```

Forbidden:

- View → Service discovery
- Service → ViewModel references
- ViewModel owning authoritative state
- Any layer reaching upward

---

# 6. Persistence Boundaries

Rules:

- Only `SaveService` writes to disk
- Services update save state through `SaveService`
- Do not save derived values
- Do not change save schema without explicit instruction

---

# 7. Composition Rules

Only Composition Roots may:

- Instantiate services
- Wire dependencies
- Order initialization

AI must not create hidden initialization or scene searches.

---

# 8. Content System Boundaries

For GameDefinition / content:

Do not:

- Add fallback behavior for missing data
- Silently ignore invalid references
- Invent default values

Prefer:

- Explicit validation
- Clear errors
- Fail loud on load

---

# 9. When Scope Is Unclear

AI should:

1. Choose the smallest safe implementation
2. Avoid guessing architecture changes
3. Add a TODO where necessary
4. Stop and wait for confirmation if the change would affect multiple systems

---

# 10. Large Refactors

Allowed **only when explicitly requested**, such as:

- “Rename Generator to Node”
- “Refactor SaveService”
- “Move to catalog-based architecture”

Without explicit instruction, assume:

> Large refactors are **out of scope**.

---

# Summary

AI should prefer:

Small change
→ Compile
→ Run
→ Preserve behavior
→ Stop

Spark Plug development prioritizes:
- Stability
- Explicit architecture
- Incremental evolution

````
