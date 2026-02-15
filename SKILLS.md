---
document_role: policy
topic: ai
audience: ai
scope: repository
status: active
---

# Spark Plug – AI Skills Profile

This file defines the **domain expertise and priorities** an AI assistant should apply when working in this repository.

Unlike `AI_RULES.md`, which defines constraints, this document describes **what the assistant should be good at** and what kinds of solutions are most valuable.

The goal is to produce suggestions that match the design philosophy of Spark Plug.

---

## 1. Project Context

Spark Plug is a **data-driven idle game engine** built in Unity.

Core characteristics:

- Authoritative domain Services
- Explicit CompositionRoots
- Reactive UI (UniRx-style)
- Dumb Views
- Centralized persistence
- Content driven by Google Sheets → JSON
- Long-term engine reuse across multiple games

The assistant should prioritize **engine-quality solutions**, not one-off game hacks.

## 1.1 Primary Optimization Goals

When proposing solutions, prioritize (in order):

1. Simulation correctness
2. Architectural clarity
3. Data-driven extensibility
4. Runtime performance
5. Minimal surface area of change
6. Developer readability

If tradeoffs are required, preserve higher-priority goals.

---

## 2. Domain Expertise

The assistant should reason with knowledge of:

### Idle / Incremental Game Systems

- Generator cycle mechanics
- Manual vs automated collection
- Progress percentage vs elapsed time
- Mid-cycle speed changes
- Output scaling and multipliers
- Prestige and meta-progression concepts
- Large-number growth patterns

Key principle:

**Simulation correctness is more important than visual behavior.**

---

## 3. Simulation Model Expectations

- Simulation timing lives in Services
- UI must not drive simulation
- Progress should be percentage-based, not time-based when durations change
- Derived values must not be saved
- Systems must behave correctly at very fast cycle speeds (< 1s)

Performance awareness:

- Expect 10–100 concurrent generators
- Avoid per-frame allocations
- Prefer deterministic behavior

---

## 4. Architecture Expertise

The assistant should be comfortable working with:

### Service-Oriented Domain Model

- Services own authoritative state
- ViewModels adapt state for UI
- Views are presentation only
- SaveService is the only persistence authority

### Dependency Direction

```
View → ViewModel → Service → SaveService
```

No reverse dependencies.

### Composition Roots

- All construction and wiring occurs in CompositionRoots
- No scene searches
- No implicit globals

---

## 5. Reactive Patterns

Expected patterns:

- `IObservable<T>`
- `IReadOnlyReactiveProperty<T>`
- Forwarding values from Services
- Avoid introducing new authoritative state in ViewModels
- Prefer stream transformations over polling

The assistant should avoid:

- Imperative UI state synchronization
- Duplicate state storage

---

## 6. Data-Driven Design

Content authority:

```
Google Sheets
    ↓
Importer (Editor)
    ↓
Content JSON (imported from Google Sheets)
    ↓
Runtime (read-only)
```

Expectations:

- IDs are stable and unique
- Content changes come from Sheets, not code
- Importers should validate aggressively
- Unknown data should produce warnings or errors

The assistant should prefer **data solutions over hardcoded logic**.

Content is authoritative.

The assistant must not:

- Hardcode content values
- Special-case specific IDs
- Add gameplay behavior outside data-driven systems

---

## 7. Economy Design Patterns

The assistant should be familiar with:

- Multiplicative stacking
- Global vs local modifiers
- Manager-style automation upgrades
- Output vs speed upgrades
- Effective cycle duration calculations
- Fact-based persistence (levels, purchases, ownership)

Avoid:

- Saving multipliers
- Saving derived rates
- Mixing UI formatting with economy logic

---

## 8. UI Philosophy

UI is:

- Reactive
- Visual-only
- Non-authoritative

Views may:

- Animate smoothly
- Interpolate values
- Continue visual progress after simulation completes

Views must not:

- Change simulation state directly
- Calculate gameplay values
- Depend on timing outside Services

---

## 9. Problem-Solving Style

Preferred approach:

- Small, localized changes
- Explicit ownership of behavior
- Clear naming over clever abstractions
- Additive changes over refactors
- Fail loud instead of silent fallback

Avoid:

- Premature abstraction
- Framework introduction
- Pattern changes without request

---

## 10. Performance Awareness

Assume:

- Multiple generators active simultaneously
- Frequent reactive updates
- Mobile-class performance targets

Prefer:

- Cached calculations
- Event-driven updates
- Minimal allocations

---

## 11. Long-Term Engine Mindset

Spark Plug is intended to become a reusable engine.

The assistant should prioritize:

- Reusable components
- Clear boundaries
- Separation of domain and presentation
- Data-driven extensibility
- Maintainability over speed of implementation

---

## 12. When Unsure

Default to:

- Keeping logic in Services
- Keeping UI dumb
- Keeping data authoritative
- Asking for clarification rather than introducing architectural changes

---

## 13. Editing Bias

When modifying code:

- Prefer minimal, surgical changes
- Do not rename types or files unless requested
- Do not introduce new abstractions unless necessary
- Preserve existing architecture patterns
- Avoid broad refactors
- Avoid changing public interfaces without justification

If a larger change seems required, explain before modifying.

---

## 14. Avoid Introducing

Unless explicitly requested, do not introduce:

- Dependency injection frameworks
- Third-party libraries
- ECS/DOTS conversions
- New architectural layers
- ScriptableObject-based runtime state
- Global singletons

---

## 15. Development Phase Awareness

Current focus: vertical slice.

Prefer:

- Implementing minimal working behavior
- Supporting a small content set
- Avoiding premature generalization
- Enabling fast iteration through data

Defer scalability improvements unless requested.

---

## 16. Rename Safety (Unity)

Unity relies on type names, file names, and serialized field names.

Renaming can break:

- Scene references
- Prefab bindings
- Serialized data
- Inspector assignments
- Script–file relationships

The assistant must:

- **Never rename classes, files, or namespaces unless explicitly requested**
- **Never rename serialized fields**
- **Never change public member names without strong justification**
- Assume renames are **high-risk operations**

If a rename would improve clarity:

- Suggest it first
- Do not apply it automatically

Prefer additive changes over renaming.
