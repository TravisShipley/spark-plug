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
pack_v0_2.json
    ↓
Runtime (read-only)
```

Expectations:

- IDs are stable and unique
- Content changes come from Sheets, not code
- Importers should validate aggressively
- Unknown data should produce warnings or errors

The assistant should prefer **data solutions over hardcoded logic**.

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
