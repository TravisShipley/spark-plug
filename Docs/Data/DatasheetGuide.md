---
document_role: process
topic: content
audience: designers, developers
scope: content
status: active
---

# Spark Plug - Datasheet Guide

This document describes each Google Sheets tab used to author a Spark Plug game definition.

Each tab maps directly to a section of `spark_plug_definition_schema.json`.

Design goals:

- Sheets are designer-friendly
- JSON is engine-friendly
- Gameplay data only (Display handled separately)
- Bracket form is required for parameterized paths (e.g. `nodeOutput[currencySoft]`)

---

# Table Overview

| Sheet         | Purpose                                |
| ------------- | -------------------------------------- |
| Resources     | Defines currencies and inventory types |
| Phases        | High-level progression stages          |
| Zones         | Game worlds or regions                 |
| ComputedVars  | Derived numeric values                 |
| Nodes         | Producer / converter definitions       |
| NodeInstances | Placed nodes in zones                  |
| Links         | Resource flow between nodes            |
| Modifiers     | All gameplay effects (the core system) |
| Upgrades      | Purchasable effects                    |
| Milestones    | Level-based rewards                    |
| Projects      | Time-based upgrades                    |
| UnlockGraph   | Progression gating                     |
| Buffs         | Temporary effects                      |
| Prestige      | Meta-progression                       |

---

# Resources

## Purpose

Defines all currencies and inventory types used by the game.

## Columns

| Column        | Description                                            |
| ------------- | ------------------------------------------------------ |
| id            | Unique resource id                                     |
| displayName   | Player-facing name                                     |
| kind          | softCurrency / hardCurrency / metaCurrency / inventory |
| format.style  | currency / suffix / plain                              |
| format.symbol | Optional symbol                                        |
| stackLimit    | Max amount (null = unlimited)                          |

## Notes

- Referenced everywhere via `resourceId`
- Must be stable once live

---

# Phases

## Purpose

Represents high-level progression stages (early, mid, late game).

## Columns

| Column      | Description |
| ----------- | ----------- |
| id          | Phase id    |
| displayName | Name        |
| description | Optional    |

## Used by

- Node requirements
- Modifier conditions

---

# Zones

## Purpose

Represents a world, planet, or major game area.

## Columns

| Column          | Description                      |
| --------------- | -------------------------------- |
| id              | Zone id                          |
| displayName     | Name                             |
| description     | Optional                         |
| startingPhaseId | Initial phase                    |
| localResources  | Resources available in this zone |
| tags            | Optional categorization          |

---

# ComputedVars

## Purpose

Designer-authored formulas used by nodes, modifiers, or prestige.

## Columns

| Column          | Description                       |
| --------------- | --------------------------------- |
| id              | Variable id                       |
| zoneId          | Scope                             |
| base            | Base value                        |
| min / max       | Optional clamps                   |
| rounding        | none / floor / truncate / bankers |
| expression.type | Formula type                      |
| expression.args | Arguments                         |
| dependsOn       | Dependencies                      |

## Path Inputs

Uses bracket form:

```
resource[currencySoft]
var.otherVar
state.run.key
```

---

# Nodes

## Purpose

Defines production units (generators, converters, etc.)

## Columns

### Identity

| Column      | Description                              |
| ----------- | ---------------------------------------- |
| id          | Node type id                             |
| type        | producer / converter / bottleneck / sink |
| displayName | Name                                     |
| zoneId      | Zone                                     |
| tags        | Optional                                 |

---

### Inputs / Outputs

Outputs example:

```
resource
mode (rate | cycle)
basePerSecond
basePayout
amountPerCycle
```

---

### Cycle

```
cycle.baseDurationSeconds
```

---

### Capacities

```
capacities[resourceId]
```

---

### Leveling

Includes:

- levelResource
- baseLevel
- maxLevel
- priceCurve
- outputScaling
- capacityScaling

Scaling targets use bracket form:

```
outputs[currencySoft].basePerSecond
capacities[currencySoft].baseCapacity
```

---

### Automation

```
policy: alwaysOn | manualCollect | autoRepeat | manualStart
autoCollect
autoRestart
```

---

### Requirements

Unlock conditions such as:

- resourceAtLeast
- nodeLevelAtLeast
- phaseAtLeast

---

## Node Subtables

Some node properties support multiple rows per node and are authored in separate sheets.  
Each subtable must include a `nodeId` column to associate the row with its parent node.

If a property can exist multiple times for a node (for example, multiple outputs or capacities), it belongs in a subtable.

---

### NodeInputs

Defines resources consumed by a node.

| Column                  | Description                 |
| ----------------------- | --------------------------- |
| nodeId                  | Parent node id              |
| resource                | Resource consumed           |
| amountPerCycle          | Fixed amount per cycle      |
| amountPerCycleFromVar   | Optional variable reference |
| amountPerCycleFromState | Optional state path         |

**Behavior**

- Inputs are consumed at cycle start.
- Multiple input rows are allowed per node.

---

### NodeOutputs

Defines resources produced by a node.

| Column                  | Description                 |
| ----------------------- | --------------------------- |
| nodeId                  | Parent node id              |
| resource                | Resource produced           |
| mode                    | rate or cycle               |
| basePerSecond           | Used when mode = rate       |
| basePayout              | Used when mode = cycle      |
| amountPerCycle          | Fixed cycle output          |
| amountPerCycleFromVar   | Optional variable reference |
| amountPerCycleFromState | Optional state path         |

**Behavior**

- Defines the base production before modifiers are applied.
- Multiple output rows are allowed per node.

---

### NodeCapacities

Defines storage or throughput limits for a node.

| Column                | Description                     |
| --------------------- | ------------------------------- |
| nodeId                | Parent node id                  |
| resource              | Resource stored or limited      |
| capacityMode          | capacity or throughputPerSecond |
| baseCapacity          | Maximum storage                 |
| baseCapacityPerSecond | Maximum flow rate               |

---

### NodeOutputScaling

Per-level scaling applied to node outputs.

| Column | Description              |
| ------ | ------------------------ |
| nodeId | Parent node id           |
| target | Output property to scale |
| type   | add or multiply          |
| value  | Per-level change         |

**Target examples**

```
outputs[currencySoft].basePerSecond
outputs[currencySoft].basePayout
```

Bracket syntax is required.

---

### NodeCapacityScaling

Per-level scaling applied to node capacities.

| Column | Description                |
| ------ | -------------------------- |
| nodeId | Parent node id             |
| target | Capacity property to scale |
| type   | add or multiply            |
| value  | Per-level change           |

**Target examples**

```
capacities[currencySoft].baseCapacity
capacities[currencySoft].baseCapacityPerSecond
```

Bracket syntax is required.

---

### Design Rule

If a node property can appear multiple times for a single node, it should be authored in a subtable rather than as columns on the `Nodes` sheet.

---

# NodeInstances

## Purpose

Places nodes into a zone.

## Columns

| Column               | Description      |
| -------------------- | ---------------- |
| id                   | Instance id      |
| nodeId               | Node type        |
| zoneId               | Zone             |
| displayNameOverride  | Optional         |
| tags                 | Optional         |
| initialState.level   | Starting level   |
| initialState.enabled | Enabled at start |

---

# Links

## Purpose

Defines resource flow between nodes.

## Columns

| Column                | Description              |
| --------------------- | ------------------------ |
| from / to             | instanceId or nodeTypeId |
| resource              | Resource id              |
| mode                  | push / pull              |
| priority              | Order                    |
| lossFactor            | Efficiency               |
| capacityPerSecondFrom | Optional state/var       |
| enabledRequirements   | Conditions               |

---

# Modifiers (Core System)

## Purpose

All gameplay effects live here.

Everything else (upgrades, milestones, buffs) references modifiers.

---

## Columns

| Column     | Description                                           |
| ---------- | ----------------------------------------------------- |
| id         | Modifier id                                           |
| source     | upgradeId / milestoneId / buffId / projectId / system |
| zoneId     | Scope zone                                            |
| scope.kind | global / zone / node / nodeTag / resource             |
| operation  | add / multiply / set / clampMin / clampMax            |
| target     | What changes                                          |
| value      | Numeric value                                         |

---

## Target Grammar

Bracket form required:

```
nodeOutput[currencySoft]
resourceGain[currencySoft]
nodeCapacity[currencySoft].throughputPerSecond
nodeSpeedMultiplier
automation.policy
```

Scope controls WHERE it applies.

---

# Upgrades

## Purpose

Purchasable permanent effects.

## Columns

| Column          | Description                                         |
| --------------- | --------------------------------------------------- |
| id              | Upgrade id                                          |
| displayName     | Name                                                |
| category        | node / global / automation / prestige / progression |
| zoneId          | Zone                                                |
| cost            | Resource cost                                       |
| repeatable      | True/False                                          |
| maxRank         | Max levels                                          |
| rankCostScaling | Cost growth                                         |
| effects         | modifierIds                                         |
| requirements    | Unlock conditions                                   |
| tags            | Optional                                            |

---

# Milestones

## Purpose

Automatic rewards when a node reaches a level.

## Columns

| Column       | Description   |
| ------------ | ------------- |
| id           | Milestone id  |
| nodeId       | Node type     |
| zoneId       | Zone          |
| atLevel      | Trigger level |
| grantEffects | modifierIds   |

---

# Projects

## Purpose

Timed upgrades.

## Columns

| Column         | Description                       |
| -------------- | --------------------------------- |
| id             | Project id                        |
| displayName    | Name                              |
| zoneId         | Zone                              |
| timeSeconds    | Duration                          |
| cost           | Resource cost                     |
| effects        | modifierIds                       |
| requirements   | Unlock conditions                 |
| exclusiveGroup | Optional mutually exclusive group |
| tags           | Optional                          |

---

# UnlockGraph

## Purpose

Controls progression and content unlocking.

## Columns

| Column       | Description                                                       |
| ------------ | ----------------------------------------------------------------- |
| id           | Unlock id                                                         |
| zoneId       | Zone                                                              |
| unlocks.kind | node / nodeInstance / upgrade / project / zone / phase / modifier |
| unlocks.id   | Target id                                                         |
| requirements | Conditions                                                        |

---

# Buffs

## Purpose

Temporary effects (ads, events, boosts).

## Columns

| Column          | Description                       |
| --------------- | --------------------------------- |
| id              | Buff id                           |
| displayName     | Name                              |
| zoneId          | Zone                              |
| durationSeconds | Duration                          |
| stacking        | refresh / extend / ignore / stack |
| effects         | modifierIds                       |

---

## Behavior

| Mode    | Meaning                    |
| ------- | -------------------------- |
| refresh | Reset duration             |
| extend  | Add duration               |
| ignore  | Do nothing if active       |
| stack   | Multiple instances allowed |

---

# Prestige

## Purpose

Meta-progression system.

---

## Core Fields

| Column           | Description   |
| ---------------- | ------------- |
| enabled          | Enable system |
| zoneId           | Zone          |
| prestigeResource | Meta currency |

---

## Formula

```
type: sqrt | log10 | linear | customExpression
basedOn: lifetimeEarnings[resourceId] | resource[resourceId] | state.path
multiplier
offset
```

Bracket form required.

---

## ResetScopes

Controls what resets.

---

## MetaUpgrades

Permanent upgrades purchased with prestige currency.

Fields:

- id
- displayName
- computed (cost formula)
- writesToState

---

# Bracket Form Rule (Global)

Always use:

```
baseName[resourceId]
```

Examples:

Correct:

```
nodeOutput[currencySoft]
resourceGain[currencySoft]
resource[currencyMeta]
lifetimeEarnings[currencySoft]
```

Incorrect:

```
nodeOutput.currencySoft
resource.currencyMeta
```

---

# Design Principles

1. Modifiers contain all gameplay math
2. Other systems reference modifiers
3. Scope controls WHERE
4. Target controls WHAT
5. Bracket form parameterizes targets
6. Sheets are authoritative for content

---

# Authoring Workflow

Sheets → Importer → JSON → Validator → Runtime

Validation should ensure:

- All referenced ids exist
- Bracket form is correct
- No orphan modifiers
- Costs and values are valid numbers

---

# Future Extensions

- Display data (separate pack)
- Event-specific sheets
- A/B testing columns
- Content versioning
