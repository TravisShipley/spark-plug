---
document_role: policy
topic: architecture
audience: ai, developers
scope: architecture
status: active
---

# Spark Plug – Architecture & Naming Conventions

## 1. High-level architecture

Spark Plug is structured around **authoritative domain services**, **explicit composition roots**, and a **dumb, reactive UI**.

- **Domain services** own game state and rules.
- **SaveService** is the single authority over persistence and disk IO.
- **ViewModels** adapt domain state for UI consumption.
- **Views** render and forward intent only.
- **Composition roots** wire everything together once at startup.

No scene searching. No hidden globals. No UI-owned game logic.

---

## 2. Naming conventions

### 2.1 Services

**Rule:** All authoritative systems end in `Service`.

Examples:

- `WalletService`
- `GeneratorService`
- `UpgradeService`
- `UiScreenService`
- `TickService`
- `SaveService`

**Guidelines:**

- Services are pure C# (no `MonoBehaviour`)
- Created explicitly in `GameCompositionRoot`
- Own state, rules, and persistence triggers
- Never reference UI types
- Only `SaveService` may read from or write to disk (`SaveSystem`).
- Avoid naming non-authoritative helpers as `*Service` (use `Builder`/`Composer`/`Factory` instead).

---

### 2.2 ViewModels

**Rule:** UI-facing adapters end in `ViewModel`.

Examples:

- `WalletViewModel`
- `GeneratorViewModel`
- `BottomBarViewModel`

**Guidelines:**

- Contain no Unity-specific code
- Expose reactive properties (`IReadOnlyReactiveProperty<T>`)
- Expose `UiCommand`s for user intent
- Do not mutate services except through explicit methods
- Prefer delegating list/entry construction to a `Builder`/`Composer` rather than embedding complex lookup/parsing in the ViewModel.

---

### 2.3 Views

**Rule:** Scene components end in `View`.

Examples:

- `GeneratorView`
- `CurrencyView`
- `BottomBarView`

**Guidelines:**

- `MonoBehaviour` only
- No gameplay logic
- No state mutation
- Bind once via a `Bind(ViewModel)` method
- Never search the scene or access services directly

---

### 2.4 Composition Roots

**Rule:** Files that wire systems together use `CompositionRoot`.

Examples:

- `GameCompositionRoot`
- `UiCompositionRoot`

**Guidelines:**

- Created once per scene
- Responsible for construction, ordering, and binding
- The _only_ place where services and view models are instantiated
- No gameplay logic
- Construction and save-state loading are separate phases

### 2.4.1 Builders, Composers, and Factories

**Rule:** Prefer intent-based nouns like `Builder`, `Composer`, or `Factory` for _UI shaping_ and _object construction_ helpers. Avoid naming these helpers as `*Service`.

**Intent:**
These types assemble or transform existing data into a convenient shape for a caller (often a ViewModel or UI layer). They are not authoritative game systems.

**Use these suffixes:**

- `*Composer` — wires together multiple objects or creates a set of runtime objects from definitions.
  - Examples:
    - `GeneratorListComposer`
    - `UiCompositionRoot` (still a CompositionRoot, but conceptually similar)

- `*Builder` — builds a value or list from existing inputs, typically returning DTOs/ViewModels.
  - Examples:
    - `UpgradeListBuilder`
    - `UpgradeEntryBuilder`

- `*Factory` — creates instances (often one-at-a-time) with minimal transformation.
  - Examples:
    - `UpgradeEntryViewModelFactory`
    - `NodeRuntimeFactory`

**Avoid these names for helpers:**

- `*ProjectionService` (too abstract; reads like domain authority)
- `*UiService` / `*ManagerService` (vague; overlaps with authoritative `Service` meaning)

**Guidelines:**

- Builders/Composers/Factories contain **no domain authority**.
- They may depend on catalogs/definitions/services, but must not own persistent state.
- They should be easy to delete/replace without affecting save data or core rules.
- If a helper starts owning rules/state, promote it into an authoritative `*Service`.

---

## 2.5 Context and Registry objects

**Rule:** Objects that bundle references are named `Context` or `Registry`, never `Service`.

**Intent:**  
These types reduce constructor and method parameter lists by grouping related dependencies. They are structural containers only — not systems.

### Context

A `Context` represents a **read-only grouping of related data or dependencies** used together by a consumer.

**Examples:**

- `UiBindingsContext`
- `GeneratorComposeContext`

**Guidelines:**

- Immutable after construction
- No business logic
- No lifecycle management
- Passed explicitly (never discovered)
- Used when a consumer needs a small set of related values

---

### Registry

A `Registry` provides **lookup access to shared runtime services**.

**Examples:**

- `UiServiceRegistry`

**Guidelines:**

- Holds references to already-created services
- Does not create services
- Does not own service lifetimes
- Contains no business logic
- Exists only to simplify access to commonly shared services

---

### Important

- Contexts and registries are **structural helpers**, not architectural layers.
- They must not contain behavior, domain rules, or state mutations.
- If logic begins to accumulate, the type should likely become a `Service`.

---

### 2.6 Commands

**Rule:** User intent is represented by `UiCommand`.

Examples:

- `ShowUpgrades`
- `ShowPrestige`
- `Collect`
- `Build`

**Guidelines:**

- Owned by ViewModels
- May expose:
  - `CanExecute`
  - `IsVisible`
- Views bind directly to commands
- Commands never contain UI code

---

## 2.7 Persistence

**Rule:** Persistence is centralized in `SaveService`.

**Guidelines:**

- `SaveService` owns the in-memory `GameData` snapshot
- All save mutations go through explicit methods on `SaveService`
- Disk writes are debounced and scheduled internally
- Other services never call `SaveSystem` directly
- Scene reset is orchestrated by the composition root

---

## 3. Property naming

### 3.1 Prefer explicit over abbreviated

**Good:**

- `WalletViewModel`
- `UpgradeService`
- `UiScreenService`

**Avoid:**

- `WalletVM`
- `UpgSvc`
- `UIService`

---

### 3.2 Context properties match their type

Context objects should expose properties whose names match their concrete types.

Example:

- `WalletService`
- `WalletViewModel`
- `UpgradeService`
- `UiScreenService`
- `UiServiceRegistry`

This optimizes clarity at call sites and avoids abbreviation-based ambiguity.

## 4. UI state vs domain state

- **Domain state** lives in Services
- **UI state** lives in Views
- UI-only state is explicitly named and local

Examples:

- `ProgressState` (visual-only)
- `IsAnimating`, `IsWaitingForCollect` (presentation only)

UI state never affects simulation.

---

## 5. Persistence rules

- `SaveService` is the single persistence authority
- Save **facts**, not derived values
- Services mutate save state via `SaveService`, never directly
- Multipliers and timing are recomputed on load
- Scene reload is used to guarantee a clean reset

---

## 6. General principles

- Fail loud over silent fallback
- Prefer composition over inheritance
- Avoid magic numbers; name intent
- One responsibility per file
- Optimize for “readable six months later”

---

## 7. Dependency direction

Dependencies must flow inward toward domain services.

Allowed dependencies:

View → ViewModel  
ViewModel → Services  
Services → SaveService

Forbidden:

Services → ViewModel or View  
Services → other Services unless explicitly composed  
Views → Services directly  
Any class → SaveSystem (except SaveService)

---

## 8. Reactive ownership

Services own authoritative reactive state.

ViewModels may transform or combine streams but must not
introduce new authoritative state.

Views subscribe only; they never publish domain state.
