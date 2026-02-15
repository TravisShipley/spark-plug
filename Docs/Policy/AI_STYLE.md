````
---
document_role: policy
audience: ai, developers
scope: repository
status: active
---

# Spark Plug – AI Style Contract

This document defines how AI assistants (Cursor, Codex, ChatGPT, etc.) should generate and modify code in this repository.

The goal is:

- Predictable architecture
- Minimal churn
- Long-term maintainability
- Safe autonomous edits

AI should treat this as **hard constraints**, not suggestions.

---

# 1. Architectural Principles (Non-Negotiable)

## 1.1 Services are authoritative

- All game logic lives in `*Service` classes
- Services:
  - Are pure C#
  - Do not inherit from `MonoBehaviour`
  - Do not reference Unity UI
  - Do not access disk directly (only `SaveService` may)

Examples:

- `WalletService`
- `GeneratorService`
- `UpgradeService`
- `SaveService`

Never move logic into:

- Views
- ViewModels
- Composition roots

---

## 1.2 Views are dumb

Views:

- Render state
- Forward user input
- Bind once via `Bind(ViewModel)`

Views must NOT:

- Call services directly
- Contain gameplay logic
- Perform scene searches
- Mutate domain state

---

## 1.3 ViewModels adapt only

ViewModels:

- Expose reactive properties
- Expose `UiCommand`s
- Translate service state → UI state

ViewModels must NOT:

- Contain business rules
- Store authoritative game state

---

## 1.4 Composition happens once

All construction occurs in:

- `GameCompositionRoot`
- `UiCompositionRoot`

Composition roots:

- Create services
- Load save state
- Create ViewModels
- Bind Views

They must NOT:

- Contain gameplay logic
- Tick systems
- Make runtime decisions

---

# 2. Naming Rules

## 2.1 Type naming

| Type                 | Suffix            |
| -------------------- | ----------------- |
| Authoritative system | `Service`         |
| UI adapter           | `ViewModel`       |
| Scene component      | `View`            |
| Data definition      | `Definition`      |
| Runtime collection   | `Catalog`         |
| Wiring file          | `CompositionRoot` |

Examples:

- `NodeDefinition`
- `NodeCatalog`
- `UpgradeService`
- `GeneratorViewModel`

---

## 2.2 Variable naming

Prefer explicit names for long-lived values.

**Good**

```csharp
var generatorService = new GeneratorService();
```

**Avoid**

```csharp
var svc = new GeneratorService();
```

Short names are allowed for:

- Loop variables (`i`)
- LINQ lambdas (`g`)
- Short-lived locals (`dt`)

Avoid semantic noise:

- `Instance`
- `Object`
- `Data`
- `Info`
- `Manager` (unless it is actually a manager)

---

# 3. Editing Rules for AI

## 3.1 Do not rename existing public APIs unless requested

Renames create large diffs and break references.

Only rename when:

- Explicitly asked
- Performing a clearly requested refactor

---

## 3.2 Do not introduce new architectural patterns

Do NOT add:

- Service locators
- Singletons
- Event buses
- Global statics
- Dependency injection frameworks

Use explicit construction in Composition Roots.

---

## 3.3 Do not move responsibilities between layers

Examples of forbidden changes:

❌ Moving logic from Service → ViewModel
❌ Moving logic from Service → View
❌ Adding persistence outside SaveService

---

## 3.4 Minimize surface area changes

When modifying a file:

- Change only what is necessary
- Do not reformat unrelated code
- Do not reorder members without reason

---

# 4. Data Model Rules

## 4.1 Definitions are immutable

`*Definition` classes:

- Represent imported content
- Must not be mutated at runtime

Runtime state belongs in Services.

---

## 4.2 Save only facts

Save:

- Owned
- Level
- Purchased
- Unlocked

Do NOT save:

- Derived multipliers
- Calculated outputs
- Cached values

---

# 5. Unity-Specific Constraints

## 5.1 Avoid scene searches

Never use:

- `FindObjectOfType`
- `GetComponentInChildren` (global search)
- `GameObject.Find`

Dependencies must be passed during composition.

---

## 5.2 No logic in Update unless visual

Allowed:

- Progress bar animation
- Visual smoothing

Not allowed:

- Economy logic
- Timers affecting simulation

---

# 6. Modal & UI Commands

User intent must flow:

View
→ `UiCommand`
→ ViewModel
→ Service

Views must not call services directly.

---

# 7. When AI Is Unsure

Prefer:

- Smaller changes
- Asking for clarification
- Adding TODO comments

Do NOT:

- Invent new systems
- Add speculative architecture
- “Improve” structure without instruction

---

# 8. Desired AI Behavior

Good AI changes:

- Small
- Localized
- Consistent with naming
- Service-centric
- Easy to review

Bad AI changes:

- Large refactors without request
- Renaming many symbols
- Introducing new patterns
- Moving logic across layers

---

# Summary

Spark Plug architecture:

**Definitions → Catalogs → Services → ViewModels → Views**

Authority flows downward.
Dependencies flow downward.
UI never owns logic.

````
