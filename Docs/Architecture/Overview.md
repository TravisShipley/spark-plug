---
document_role: process
audience: ai, developers
scope: repository
status: active
---

# Spark Plug – Overview

For detailed rules and constraints, see:

- `ArchitectureRules.md`
- `ServiceResponsibilities.md`
- `SystemMap.md`

---

## What Spark Plug is

Spark Plug is a **data-driven idle game engine** built around three core principles:

- **Authoritative domain services**
- **Explicit composition**
- **Dumb, reactive UI**

All game behavior is defined by **data** (`game_definition.json`) and executed by **runtime services**.  
The UI reflects state and sends intent — it does not contain game logic.

---

## High-level architecture

```
GameDefinition (data)
        ↓
   Domain Services
        ↓
     ViewModels
        ↓
        Views
```

**Flow of authority**

- Data defines behavior
- Services execute the simulation
- ViewModels adapt state for UI
- Views render and forward user input

No scene searching. No hidden globals.

---

## Core layers

### Game Definition (Content)

The entire game is defined by external data:

- Nodes
- NodeInstances
- Upgrades
- Modifiers
- Unlocks, milestones, etc.

This data is imported from Google Sheets into:

```
Assets/Data/game_definition.json
```

At runtime it is loaded into:

- `GameDefinition`
- Catalogs (NodeCatalog, UpgradeCatalog, etc.)

The definition is **read-only** at runtime.

---

### Domain Services (Authoritative Systems)

Services own all game state and rules.

Examples:

- `GeneratorService`
- `WalletService`
- `UpgradeService`
- `TickService`
- `SaveService`

Responsibilities:

- Run simulation
- Apply modifiers
- Mutate persistent state via `SaveService`
- Expose authoritative reactive state

Services **never reference UI**.

---

### SaveService (Persistence Authority)

`SaveService` is the single owner of:

- In-memory `GameData`
- Disk IO (`SaveSystem`)
- Debounced saves

Other services:

- Request mutations through `SaveService`
- Never read/write disk directly

Only **facts** are saved. Derived values are recomputed on load.

---

### ViewModels (UI Adapters)

ViewModels translate service state into UI-friendly form.

They:

- Expose `IReadOnlyReactiveProperty<T>`
- Expose `UiCommand`s for user intent
- Contain no Unity-specific code

They may **call services**, but do not own game logic.

---

### Views (Unity Components)

Views are simple renderers.

Rules:

- `MonoBehaviour` only
- Bind once via `Bind(ViewModel)`
- No game logic
- No service access
- No scene searching

They:

- Subscribe to reactive properties
- Forward user actions via commands

---

### Composition Roots

Composition roots wire the system together at startup.

Examples:

- `GameCompositionRoot`
- `UiCompositionRoot`

They are responsible for:

1. Loading `GameDefinition`
2. Creating services
3. Loading save state
4. Creating ViewModels
5. Binding Views

This is the **only place** where services and ViewModels are instantiated.

---

## Dependency direction

Dependencies always flow inward:

```
View → ViewModel → Service → SaveService
```

Never the reverse.

---

## Data-driven behavior

Game balance and progression are controlled through:

- Modifiers
- Upgrades
- Unlock graphs
- Milestones
- Content packs

Code executes systems.  
**Data defines the game.**

---

## Design goals

Spark Plug optimizes for:

- Clear authority boundaries
- Predictable state flow
- Minimal hidden coupling
- Fast iteration through data
- Stability under live-ops scale
- Readability six months later

---
