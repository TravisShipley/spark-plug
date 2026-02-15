````
---
document_role: policy
audience: ai, developers
scope: repository
status: active
---

# Spark Plug – AI Task Strategy

This document defines how AI assistants should approach implementing work in this repository.

The goal is:
- Safe autonomous progress
- Small, reviewable changes
- Working vertical slices
- Minimal architectural drift

AI must follow this strategy when performing multi-step tasks.

---

# 1. Core Principle

**Always work in vertical slices.**

Each step should:
- Compile
- Run
- Not break existing behavior
- Be testable in isolation

Never leave the project in a partially-wired state.

---

# 2. Work Order Strategy

When implementing a feature, AI should follow this order:

### Step 1 — Data

Add or update:
- `Definition` classes
- `Catalog` classes
- Validation (if needed)

No runtime behavior yet.

---

### Step 2 — Service

Extend or add the relevant `*Service`.

Rules:
- Services own logic
- Services consume Definitions/Catalogs
- No UI changes yet

---

### Step 3 — Persistence (if required)

If the feature introduces new **facts**:
- Update `GameData`
- Update `SaveService`
- Load defaults correctly

Do NOT save derived values.

---

### Step 4 — ViewModel

Expose:
- Reactive properties
- `UiCommand`s

No business logic here — only adaptation.

---

### Step 5 — View

Bind UI elements.

Views must:
- Only render state
- Only forward intent

---

# 3. Change Size Rules

Each AI step should modify:

**Ideal**
- 1–3 files

**Maximum**
- 5 files

If more changes are required:
→ break the task into multiple steps.

---

# 4. Safety Rules

AI must ensure:

- Project compiles after each step
- No null-reference risks introduced
- New fields have safe defaults
- Existing save data still loads

If a breaking schema change is required:
→ add migration logic or reset handling

---

# 5. Dependency Direction

Always respect dependency flow:

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

Never reverse this direction.

Forbidden examples:

❌ View calling Service locator
❌ Service referencing ViewModel
❌ ViewModel storing authoritative state

---

# 6. Feature Completion Criteria

A feature is considered complete when:

- Data loads correctly
- Service behavior works
- UI reflects state
- State persists across reload
- No console errors
- No unused fields or dead code

---

# 7. Validation Strategy

When adding new content systems:

Prefer:
- Fail loud on load
- Explicit validation
- Clear error messages

Avoid:
- Silent fallbacks
- Defaulting to empty behavior
- Ignoring unknown references

---

# 8. Refactoring Rules

AI must NOT perform large refactors unless explicitly requested.

Allowed:
- Local cleanup
- Naming alignment
- Small extraction

Not allowed:
- Renaming many files
- Moving architecture layers
- Introducing new patterns

---

# 9. When Requirements Are Unclear

AI should:

1. Choose the smallest safe implementation
2. Add a clear TODO comment
3. Avoid guessing new systems

Example:

```csharp
// TODO: Support additional modifier operations if required by content
```

---

# 10. Testing Expectations

After each step, the slice should support a manual test path:

Example flow:
- Load game
- Perform action
- Observe result
- Reload
- Verify persistence

If no test path exists:
→ the slice is incomplete

---

# 11. Preferred Task Pattern

When given a large goal:

AI should respond with:

1. Step breakdown (small slices)
2. Implement Step 1 only
3. Wait for confirmation

---

# 12. Anti-Patterns to Avoid

Do NOT:

- Implement multiple systems at once
- Add speculative architecture
- Add “future-proof” abstractions
- Modify unrelated files
- Optimize prematurely

---

# Summary

Good AI workflow:

Small step
→ Compile
→ Run
→ Verify
→ Persist
→ Next step

Spark Plug development is **incremental, authoritative, and definition-driven**.

````
