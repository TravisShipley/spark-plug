# Spark Plug – Service Responsibilities

This document defines **which Service owns which domain behavior**.

The goal is to prevent responsibility drift and ensure that authoritative logic remains centralized and predictable.

If a feature does not clearly belong to a Service listed here, a new Service should likely be introduced.

---

## Principles

- Services own **domain state and rules**
- Services are the **only source of authoritative values**
- Services never reference UI
- Services mutate persistence **only through SaveService**
- Services should not depend directly on other Services unless explicitly composed

---

## WalletService

**Authority:** Player currency balances

### Owns

- Current balances for all resources
- Spending validation
- Earning logic

### Public Responsibilities

- `TrySpend(resource, amount)`
- `Earn(resource, amount)`
- Expose reactive balances

### Does NOT

- Know about generators or upgrades
- Apply multipliers (handled by UpgradeService / GeneratorService)
- Access disk directly

Persistence: Writes via `SaveService`

---

## GeneratorService

**Authority:** Generator simulation

### Owns

- Ownership state
- Level
- Automation state
- Running state
- Cycle timing
- Output calculation

### Authoritative Values

- `CycleDurationSeconds`
- `OutputPerCycle`
- `IsRunning`
- `IsReadyForCollect`
- `IsAutomated`
- `IsOwned`

### Responsibilities

- Start/stop cycles
- Handle manual collection
- Handle automated collection
- Apply level-based scaling
- Apply upgrade multipliers (provided externally)

### Does NOT

- Format values for UI
- Animate progress bars
- Decide UI visibility

Persistence: Writes state via `SaveService`

---

## UpgradeService

**Authority:** Purchased upgrades and their effects

### Owns

- Purchased upgrade state
- Upgrade rank (for repeatables)
- Application of upgrade effects

### Responsibilities

- Validate purchase conditions
- Deduct cost via `WalletService`
- Apply effects to target Services
- Expose reactive purchase state

### Effect Targets

- Generator speed multipliers
- Generator output multipliers
- Global economy modifiers

### Does NOT

- Store derived values permanently
- Modify UI directly

Persistence: Writes purchases via `SaveService`

---

## SaveService

**Authority:** Player persistence

### Owns

- In-memory `GameData`
- Disk read/write
- Save scheduling and debouncing

### Responsibilities

- Load save state
- Provide mutation methods for Services
- Persist changes to disk
- Reset to default state

### Rules

- Only SaveService may access `SaveSystem`
- Save **facts only**
- Never store derived or computed values

---

## TickService

**Authority:** Time progression

### Owns

- Central update stream
- Delta time distribution

### Responsibilities

- Provide a consistent time source for simulation
- Drive generator progression

### Does NOT

- Contain gameplay logic

---

## ModalService

**Authority:** UI modal orchestration (UI boundary)

### Responsibilities

- Open/close modal windows
- Provide a stable interface for ViewModels (`UiCommand` targets)

### Does NOT

- Contain game logic
- Reference domain Services

---

## PackLoaderService

**Authority:** Runtime content data

### Responsibilities

- Load imported JSON pack
- Validate content integrity
- Provide read-only access to content definitions

### Rules

- Content is immutable at runtime

---

## Composition Roots

**GameCompositionRoot**

Responsibilities:

- Construct all Services
- Load save state
- Apply content definitions
- Create ViewModels
- Bind Views

**UiCompositionRoot**

Responsibilities:

- Bind scene-level UI
- Provide UI contexts and registries

---

## Responsibility Boundaries

### If a value affects simulation → Service

Examples:

- Cycle duration
- Output amount
- Upgrade multiplier

### If a value affects only presentation → View

Examples:

- Progress bar animation
- Button transitions
- Visual timers

### If a value adapts domain state for UI → ViewModel

Examples:

- Formatted strings
- Visibility flags
- `UiCommand`

---

## Common Mistakes

❌ Calculating cycle duration in ViewModel  
❌ Applying upgrade multipliers in View  
❌ Saving derived values  
❌ Letting multiple Services write to disk  
❌ Moving logic into UI for convenience

---

## When to Create a New Service

Create a new Service if:

- State must be authoritative
- Multiple systems depend on the behavior
- The logic is not purely presentation
- The logic must be persisted or restored

Examples:

- PrestigeService
- BuffService
- EconomyService
