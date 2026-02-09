# Spark Plug – System Map

This document provides a one-page mental model of how Spark Plug is structured at runtime.

It exists to answer:

- What are the major systems?
- Who owns what state?
- How data flows from content → simulation → UI → persistence?
- Where should new behavior live?

---

## 1. Runtime Graph (high-level)

```text
Google Sheets (authoritative content)
        │
        ▼
Editor Importer (build step)
        │
        ▼
pack_v0_2.json (read-only runtime content)
        │
        ▼
PackLoaderService
        │
        ▼
GameCompositionRoot
   ├─ constructs Services
   ├─ loads SaveService snapshot
   ├─ applies content definitions
   ├─ creates ViewModels
   └─ binds Views

UI is subscribed-only:
Views bind to ViewModels and forward user intent via UiCommands.
```

---

## 2. Dependency Direction

Allowed:

```text
View → ViewModel → Service → SaveService
```

Forbidden:

- Service → ViewModel / View
- View → Service
- Any class → SaveSystem (except SaveService)
- Scene searching for dependencies

---

## 3. Composition Roots

### GameCompositionRoot

Responsibilities:

- Construct all domain services
- Construct UI services/registries as needed
- Load save snapshot (`LoadSaveState` phase)
- Apply save state into services
- Construct ViewModels
- Bind ViewModels to Views
- Orchestrate reset via SaveService + scene reload

Non-responsibilities:

- No gameplay logic
- No per-frame simulation code

### UiCompositionRoot (optional)

Responsibilities:

- Bind scene-owned UI (bottom bar, currency, modal host)
- Provide UI contexts/registries to views

---

## 4. Services Map

### PackLoaderService (content authority)

Owns:

- Loaded pack data (read-only)
- Content validation

Outputs:

- Pack definitions used to construct runtime systems

---

### SaveService (persistence authority)

Owns:

- In-memory `GameData` snapshot
- Debounced disk IO
- Reset to defaults

Rules:

- Only SaveService calls SaveSystem
- Save facts, not derived values

---

### TickService (time authority)

Owns:

- Central tick stream (delta time)

Rules:

- No gameplay logic inside TickService
- Other services subscribe and update their own state

---

### WalletService (currency authority)

Owns:

- Resource balances
- Spend/earn validation

Mutates:

- SaveService wallet facts

Does not:

- Know about generators or UI

---

### GeneratorService (simulation authority)

Owns:

- Generator state: owned/level/automation/running/ready
- Cycle timing and progression
- Output calculations (authoritative)

Exposes:

- `CycleDurationSeconds` (authoritative)
- `OutputPerCycle` (authoritative)
- `IsOwned`, `IsAutomated`, `IsRunning`, `IsReadyForCollect`

Mutates:

- SaveService generator facts

---

### UpgradeService (upgrade authority)

Owns:

- Purchased upgrades and ranks
- Application of effects (speed/output/global)

Responsibilities:

- Validate purchase conditions
- Spend currency via WalletService
- Apply effects to target services
- Persist purchases via SaveService

Does not:

- Format values for UI
- Save derived multipliers

---

### ModalService (UI boundary service)

Owns:

- Modal orchestration interface (Show/Hide commands)

Rules:

- No domain logic
- Views/ViewModels call ModalService instead of touching ModalManager directly

---

## 5. UI Map

### ViewModels (UI adapters)

Own:

- UiCommands
- Stream composition / formatting
- UI-facing properties

Do not:

- Implement simulation rules
- Compute authoritative values
- Own independent domain state

Examples:

- WalletViewModel (forwards balances)
- GeneratorViewModel (forwards generator properties)
- BottomBarViewModel (exposes modal commands)

---

### Views (Unity scene components)

Own:

- Visual-only state (animation, progress smoothing)
- Unity component refs (Text, Button, Image)
- Binding subscriptions

Do not:

- Mutate domain state directly
- Search the scene for services
- Compute gameplay values

Examples:

- CurrencyView
- GeneratorView
- BottomBarView
- Modal views

---

## 6. State Ownership Summary

| Kind of state        | Lives in          | Saved? | Notes                              |
| -------------------- | ----------------- | -----: | ---------------------------------- |
| Content definitions  | PackLoaderService |     No | Read-only at runtime               |
| Player facts         | SaveService       |    Yes | Wallet, owned, levels, purchases   |
| Simulation state     | Services          |     No | Derived and reconstructible        |
| UI-only presentation | Views             |     No | Animations, smoothing, local flags |

---

## 7. Typical Data Flow (end-to-end)

### Content → Runtime

```text
Sheets → Importer → pack_v0_2.json → PackLoaderService
```

### Simulation Tick

```text
TickService → GeneratorService (elapsed/progress) → Ready state
```

### Player action

```text
View click → UiCommand → ViewModel → Service method → SaveService mutate → debounced write
```

### UI updates

```text
Service reactive state → ViewModel forwarding/formatting → View subscription renders
```

---

## 8. Where to Add New Things

- New persistent player fact → SaveService + GameData
- New simulation system → New Service
- New UI screen/modal → View + ViewModel + ModalService command
- New content-driven feature → Sheet schema + importer + pack + service integration

---

## 9. Anti-drift Checks

If you see any of the following, restructure:

- ViewModel computing durations/multipliers
- Views calling services directly
- Services calling SaveSystem
- Scene search usage
- Multiple systems owning the same fact
