# Spark Plug – Architecture & Naming Conventions

## 1. High-level architecture

Spark Plug is structured around **authoritative domain services**, **explicit composition roots**, and a **dumb, reactive UI**.

- **Domain services** own game state and rules.
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
- `ModalService`
- `TickService`

**Guidelines:**
- Services are pure C# (no `MonoBehaviour`)
- Created explicitly in `GameCompositionRoot`
- Own state, rules, and persistence triggers
- Never reference UI types

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
- The *only* place where services and view models are instantiated
- No gameplay logic

---

### 2.5 Context / Registry Objects
**Rule:** Objects that bundle references are named `Context` or `Registry`, never `Service`.

Examples:
- `UiBindingsContext`
- `UiServiceRegistry`

**Guidelines:**
- Read-only data containers
- No business logic
- Used to reduce parameter explosion
- Explicitly passed (never discovered)

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

## 3. Property naming

### 3.1 Prefer explicit over abbreviated

**Good:**
- `WalletViewModel`
- `UpgradeService`
- `ModalService`

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
- `ModalService`
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

- Save **facts**, not derived values
- Recompute multipliers and timing on load
- Services are responsible for reapplying saved state

---

## 6. General principles

- Fail loud over silent fallback
- Prefer composition over inheritance
- Avoid magic numbers; name intent
- One responsibility per file
- Optimize for “readable six months later”