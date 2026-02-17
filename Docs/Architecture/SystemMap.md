---
document_role: design
topic: architecture
audience: ai, developers
scope: architecture
status: active
---

# System Map

This document provides a runtime mental model and ownership map.

## 1. Runtime Graph

```text
Google Sheets
  -> Importer
  -> pack_v0_2.json
  -> PackLoaderService
  -> GameCompositionRoot
      -> Services
      -> ViewModels
      -> Views
```

## 2. Data Flow

### Content to Runtime

```text
Sheets -> Importer -> Pack JSON -> Catalogs/Definitions -> Services
```

### Player Action

```text
View input -> ViewModel command -> Service mutation -> SaveService
```

### UI Refresh

```text
Service reactive state -> ViewModel projection -> View rendering
```

## 3. State Ownership

| State Type               | Owner              | Persisted |
| ------------------------ | ------------------ | --------- |
| Content definitions      | Pack/content layer | No        |
| Player facts             | SaveService        | Yes       |
| Simulation derived state | Domain services    | No        |
| UI presentation state    | Views              | No        |

## 4. Where to Add New Work

- New player fact: `GameData` + `SaveService`
- New simulation mechanic: new/extended Service
- New UI screen: ViewModel + View (+ screen wiring if needed)
- New content feature: schema/import + service integration

For constraints and forbidden patterns, see `ArchitectureRules.md`.
