---
document_role: policy
topic: workflow
audience: ai, developers
scope: repository
status: active
---

# Spark Plug – Data Authority

This document defines where each type of data lives and who can mutate it.

## 1. Content Data

| Data | Authority | Location | Mutated By |
| --- | --- | --- | --- |
| Game content (nodes, upgrades, etc.) | Google Sheets | External | Designers |
| Imported pack | JSON (`game_definition.json`) | `Assets/Data` | Importer only |

Rules:

- Treat imported content as read-only at runtime.
- Change content through sheets + importer, never manual JSON edits.

## 2. Player Facts

| Data | Authority | Location | Mutated By |
| --- | --- | --- | --- |
| Wallet balances | `SaveService` | `GameData` | Domain services via `SaveService` |
| Generator state | `SaveService` | `GameData` | Domain services via `SaveService` |
| Purchased upgrades | `SaveService` | `GameData` | `UpgradeService` via `SaveService` |

Rules:

- Only `SaveService` writes to disk.
- Only facts are persisted.

## 3. Derived Runtime State

Examples:

- Effective cycle duration
- Output multipliers
- Transient run/progress state

Authority: domain services.

Rules:

- Derived at runtime
- Not persisted as facts
- Reconstructed on load

## 4. UI Presentation State

Examples:

- Progress animation smoothing
- Button visibility transitions
- Local visual timers

Authority: views.

Rules:

- Never authoritative for simulation
- Never persisted

## 5. Reset Authority

Reset behavior:

1. Clear persisted facts
2. Rebuild runtime graph from content + defaults
3. Resume simulation from reconstructed state

Related docs:

- `../Architecture/ArchitectureRules.md` (constraints)
- `../Architecture/SystemMap.md` (runtime flow)
- `ContentImportWorkflow.md` (import process)
