---
document_role: reference
audience: ai, developers
scope: data
status: active
---

# Schema Dependency Map

This document lists table-to-table relationships in the spreadsheet schema.

## Core Structure

```text
PackMeta
├─ Resources
├─ Phases
└─ Zones
```

## Production Graph

```text
Nodes
├─ NodeOutputs
├─ NodeInputs
├─ NodeCapacities
├─ NodeOutputScaling
├─ NodeCapacityScaling
├─ NodePriceCurveTable
├─ NodePriceCurveSegments
└─ NodeRequirements

NodeInstances
└─ references Nodes
```

## Economy Layer

```text
Modifiers
   ▲
   │
Upgrades
Milestones
Buffs
Projects
Prestige
```

## Progression Layer

```text
UnlockGraph
├─ NodeInstances
├─ Upgrades
└─ Projects
```

## Resource Flow

```text
Links
├─ from: NodeInstance
├─ to: NodeInstance
└─ resource: Resources
```

## Key Reference Paths

| Table         | References                  |
| ------------- | --------------------------- |
| NodeOutputs   | Nodes, Resources            |
| NodeInputs    | Nodes, Resources            |
| NodeInstances | Nodes, Zones                |
| Modifiers     | Nodes/Resources (via scope) |
| Upgrades      | Modifiers                   |
| Milestones    | Nodes, Modifiers            |
| Buffs         | Modifiers                   |
| Projects      | Modifiers                   |
| Prestige      | Resources                   |

For operational import steps, see `Docs/Process/ContentImportWorkflow.md`.
