# Spark Plug – Schema Dependency Map

This document shows relationships between spreadsheet tables.

Understanding dependencies helps prevent broken references and helps maintain a correct import order.

---

## Core Structure

```
PackMeta
├─ Resources
├─ Phases
└─ Zones
```

---

## Production Graph

```
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
└─ (references Nodes)
```

---

## Economy Layer

```
Modifiers
   ▲
   │
Upgrades
Milestones
Buffs
Projects
Prestige
```

Modifiers are the central effect system.  
Most progression systems grant or enable modifiers.

---

## Progression Layer

```
UnlockGraph
├─ NodeInstances
├─ Upgrades
└─ Projects
```

---

## Resource Flow (optional)

```
Links
├─ from: NodeInstance
├─ to:   NodeInstance
└─ resource: Resources
```

---

## Key Reference Paths

| Table         | References                    |
| ------------- | ----------------------------- |
| NodeOutputs   | Nodes, Resources              |
| NodeInputs    | Nodes, Resources              |
| NodeInstances | Nodes, Zones                  |
| Modifiers     | Nodes / Resources (via scope) |
| Upgrades      | Modifiers                     |
| Milestones    | Nodes, Modifiers              |
| Buffs         | Modifiers                     |
| Projects      | Modifiers                     |
| Prestige      | Resources                     |

---

## Recommended Import Order

```
1. PackMeta
2. Resources
3. Phases
4. Zones
5. Nodes
6. NodeOutputs / Inputs / Scaling / Capacities
7. NodeInstances
8. Modifiers
9. Upgrades / Milestones / Buffs / Projects
10. UnlockGraph
11. Prestige
```

---

## Dependency Guidelines

- A referenced ID must exist before it is used.
- Modifiers should be imported before any system that references them.
- NodeInstances should only reference valid Node types.
- UnlockGraph should be processed after all unlock targets exist.
- Unknown references should fail import loudly.
