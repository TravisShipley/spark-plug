---
document_role: process
topic: workflow
audience: ai, developers
scope: content
status: active
---

# Content Import Workflow

This document describes the operational sequence for importing spreadsheet content.

## 1. Authoring Prerequisites

- IDs are stable and unique.
- Enum-backed fields use `__Enums` values.
- `_json` fields are valid JSON.

## 2. Recommended Import Order

1. PackMeta
2. Resources
3. Phases
4. Zones
5. Nodes
6. NodeOutputs/NodeInputs/Scaling/Capacities
7. NodeInstances
8. Modifiers
9. Upgrades/Milestones/Buffs/Projects
10. UnlockGraph
11. Prestige

## 3. Validation Checks

- Referenced IDs exist before use.
- Modifier references resolve.
- Unlock targets exist.
- Unknown references fail import.

## 4. Safe Workflow

1. Edit sheets.
2. Run importer.
3. Fix validation errors.
4. Commit sheet + generated artifacts.
