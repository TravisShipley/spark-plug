---
document_role: design
topic: architecture
audience: ai, developers
scope: architecture
status: active
---

# Spark Plug – Service Responsibilities

This document maps domain ownership to services.
For constraints and forbidden patterns, use `ArchitectureRules.md`.

## Ownership Summary

| Service | Owns | Writes Facts To |
| --- | --- | --- |
| `WalletService` | Currency balances, spend/earn validation | `SaveService` |
| `GeneratorService` | Generator state, cycle timing, output calculation | `SaveService`, `StateVarService` |
| `StateVarService` | Reactive zone-level state quantities and capacity-aware mutation | `SaveService` |
| `UpgradeService` | Purchase state/rank and effect application | `SaveService` |
| `SaveService` | In-memory `GameData`, session-scoped disk persistence scheduling | Disk (`SaveSystem`) |
| `TickService` | Shared simulation time stream | n/a |
| `UiScreenService` | Screen orchestration boundary for UI | n/a |
| `GameDefinitionService` | Runtime game-definition loading, catalogs, and lookups | n/a |

## Detailed Boundaries

### WalletService

- Owns balances and spend/earn rules.
- Does not know generator simulation or UI rendering.

### GeneratorService

- Owns ownership/level/automation/running state and cycle progression.
- Exposes authoritative timing/output values.
- Applies node `stateDelta` outputs via `StateVarService`.
- Does not format UI strings or animation behavior.

### StateVarService

- Owns runtime access to zone-level state quantities such as `stateQuantity[varId]`.
- Exposes reactive observation for UI/view-model projection.
- Clamps quantity mutation against resolved zone capacities before persisting via `SaveService`.
- Does not talk to disk directly.

### UpgradeService

- Owns purchased state and rank progression.
- Validates purchase conditions and applies upgrade effects.
- Does not own UI presentation.

### SaveService

- Owns persistence boundary and disk write scheduling.
- Only service allowed to touch `SaveSystem`.
- Persists player facts into a session-scoped save namespace derived from `sessionId` and `saveSlotId`.
- Remains the disk authority even when another runtime service, such as `StateVarService`, owns the in-memory mutation API for a fact type.

### TickService

- Provides a central tick source.
- Contains no game economy or progression rules.

### UiScreenService

- UI boundary for opening/closing screens.
- Contains no domain simulation logic.

### GameDefinitionService

- Loads and validates content definition data (via `GameDefinitionLoader`).
- Exposes read-only runtime catalogs/lookups used by services and UI composition.

Related docs:

- `SystemMap.md` (runtime flow)
- `SimulationModel.md` (generator lifecycle/timing details)
- `../Process/DataAuthority.md` (fact ownership + storage)
