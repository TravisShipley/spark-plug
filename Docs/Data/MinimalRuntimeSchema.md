---
document_role: reference
topic: data
audience: ai, developers
scope: data
status: active
---

# Spark Plug – Minimal Runtime Schema

This is the smallest set of tables required to run a basic idle game.

Use this for early development or small prototypes.

---

## Required Tables

### PackMeta

Global configuration.

---

### Resources

Defines currencies.

---

### Zones

At least one zone is required.

---

### Nodes

Defines generator types.

Required fields:

- id
- displayName
- cycle.baseDurationSeconds
- leveling.priceCurve.\*

---

### NodeOutputs

Defines generator payouts.

Required:

- nodeId
- resource
- basePayout

---

### NodeInstances

Creates playable generators.

Required:

- id
- nodeId
- initialState.level
- initialState.enabled

---

## Optional but Recommended

### Modifiers

For upgrades and milestones.

---

### Upgrades

For progression.

---

### Prestige

For long-term scaling.

---

## Not Required (early phase)

- NodeInputs
- Capacities
- Links
- Projects
- Buffs
- ComputedVars

---

## Minimal Game Flow

```
Nodes → produce Resources
Upgrades → apply Modifiers
Prestige → resets progress with multiplier
```

---

## Guideline

Start with minimal tables and expand only when a system is needed.
Avoid implementing unused schema features.
