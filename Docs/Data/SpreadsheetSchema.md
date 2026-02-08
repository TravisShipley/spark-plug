# Spark Plug – Google Sheets Schema (v0.2)

This document describes the structure of the Spark Plug content spreadsheet used to define game data.

The sheet is treated as **authoritative content data** and is imported into the runtime pack format.

---

## Design Principles

- Each tab represents a single domain table
- Column names use **dot notation** for nested JSON fields
- Rows represent records
- JSON columns (`*_json`) contain structured objects or arrays
- Unknown tabs are ignored by the importer
- Rows or tabs starting with `_` are treated as comments and ignored

---

## Sheet Overview

| Tab                    | Purpose                                       |
| ---------------------- | --------------------------------------------- |
| PackMeta               | Global pack settings                          |
| Resources              | Currency and resource definitions             |
| Phases                 | Progression phases                            |
| Zones                  | World/grouping context                        |
| ComputedVars           | Derived values                                |
| Nodes                  | Generator / production definitions            |
| NodeInputs             | Per-cycle resource costs                      |
| NodeOutputs            | Production outputs                            |
| NodeCapacities         | Storage limits                                |
| NodeOutputScaling      | Per-level output scaling                      |
| NodeCapacityScaling    | Per-level capacity scaling                    |
| NodePriceCurveTable    | Explicit level pricing                        |
| NodePriceCurveSegments | Segmented pricing curves                      |
| NodeRequirements       | Unlock requirements                           |
| NodeInstances          | Runtime instances of node types               |
| Links                  | Resource flow connections                     |
| Modifiers              | Effects applied by upgrades, milestones, etc. |
| Upgrades               | Purchasable effects                           |
| Milestones             | Level-based rewards                           |
| Projects               | Timed actions                                 |
| UnlockGraph            | Progression unlock rules                      |
| Buffs                  | Temporary effects                             |
| Prestige               | Reset and meta-progression rules              |

---

# Sheet Definitions

---

## PackMeta

Defines global pack metadata.

| Column                 | Description            |
| ---------------------- | ---------------------- |
| gameId                 | Unique pack identifier |
| version                | Schema/content version |
| numberFormat.type      | Formatting style       |
| numberFormat.precision | Display precision      |

---

## Resources

Defines currencies and stackable resources.

| Column        | Description                                |
| ------------- | ------------------------------------------ |
| id            | Unique resource id                         |
| displayName   | UI name                                    |
| kind          | softCurrency / hardCurrency / metaCurrency |
| format.style  | currency / suffix / plain                  |
| format.symbol | Display symbol                             |
| stackLimit    | Optional max amount                        |

---

## Phases

Optional progression phases.

| Column      | Description          |
| ----------- | -------------------- |
| id          | Phase id             |
| displayName | Name                 |
| description | Optional description |

---

## Zones

Logical grouping for nodes and progression.

| Column                      | Description                  |
| --------------------------- | ---------------------------- |
| id                          | Zone id                      |
| displayName                 | Name                         |
| presentation.descriptionKey | Localization key             |
| presentation.iconId         | Icon reference               |
| presentation.imageId        | Image reference              |
| description                 | Optional text                |
| startingPhaseId             | Initial phase                |
| localResources              | Comma-separated resource ids |
| tags                        | Comma-separated tags         |

---

## ComputedVars

Defines derived variables.

| Column          | Description      |
| --------------- | ---------------- |
| id              | Variable id      |
| displayName     | Name             |
| zoneId          | Scope            |
| base            | Default value    |
| min / max       | Optional bounds  |
| rounding        | Rounding mode    |
| expression.type | Calculation type |
| expression.args | JSON args        |
| dependsOn       | Dependencies     |

---

## Nodes

Defines generator / producer types.

| Column                    | Description                |
| ------------------------- | -------------------------- |
| id                        | Node type id               |
| type                      | producer / converter / etc |
| displayName               | UI name                    |
| presentation.\*           | Optional visual fields     |
| zoneId                    | Owning zone                |
| tags                      | Comma-separated            |
| cycle.baseDurationSeconds | Base cycle time            |
| leveling.levelResource    | Currency used to level     |
| leveling.baseLevel        | Starting level             |
| leveling.maxLevel         | Optional cap               |
| leveling.priceCurve.\*    | Pricing configuration      |
| automation.policy         | manualCollect / autoRepeat |
| automation.autoCollect    | bool                       |
| automation.autoRestart    | bool                       |

---

## NodeInputs

Per-cycle resource consumption.

| Column                  | Description       |
| ----------------------- | ----------------- |
| nodeId                  | Node type id      |
| resource                | Resource id       |
| amountPerCycle          | Fixed cost        |
| amountPerCycleFromVar   | From computed var |
| amountPerCycleFromState | From state path   |

---

## NodeOutputs

Production definitions.

| Column           | Description       |
| ---------------- | ----------------- |
| nodeId           | Node type id      |
| resource         | Resource id       |
| mode             | cycle / perSecond |
| basePerSecond    | Continuous output |
| basePayout       | Per-cycle output  |
| amountPerCycle\* | Advanced sources  |

---

## NodeCapacities

Storage limits.

| Column                | Description         |
| --------------------- | ------------------- |
| nodeId                | Node id             |
| resource              | Resource            |
| capacityMode          | fixed / perSecond   |
| baseCapacity          | Max storage         |
| baseCapacityPerSecond | Rate-based capacity |

---

## NodeOutputScaling

Per-level output scaling.

| Column         | Description    |
| -------------- | -------------- |
| nodeId         | Node id        |
| target         | Path to value  |
| perLevel.type  | multiply / add |
| perLevel.value | Scaling value  |

---

## NodeCapacityScaling

Per-level capacity scaling.

(Same structure as NodeOutputScaling)

---

## NodePriceCurveTable

Explicit price per level.

| Column | Description |
| ------ | ----------- |
| nodeId | Node id     |
| level  | Level       |
| price  | Cost        |

---

## NodePriceCurveSegments

Segmented pricing curves.

| Column              | Description          |
| ------------------- | -------------------- |
| nodeId              | Node id              |
| fromLevel / toLevel | Range                |
| curve.type          | exponential / linear |
| curve.basePrice     | Starting cost        |
| curve.growth        | Multiplier           |
| curve.increment     | Linear increment     |

---

## NodeRequirements

Unlock conditions.

| Column    | Description      |
| --------- | ---------------- |
| nodeId    | Node id          |
| type      | Requirement type |
| args_json | JSON parameters  |

---

## NodeInstances

Runtime instances of node types.

| Column               | Description    |
| -------------------- | -------------- |
| id                   | Instance id    |
| nodeId               | Node type      |
| zoneId               | Zone           |
| displayNameOverride  | Optional       |
| tags                 | Tags           |
| initialState.level   | Starting level |
| initialState.enabled | Enabled flag   |

---

## Links

Resource flow between nodes.

| Column                   | Description  |
| ------------------------ | ------------ |
| id                       | Link id      |
| zoneId                   | Zone         |
| from / to                | Node ids     |
| resource                 | Resource     |
| mode                     | Flow mode    |
| priority                 | Ordering     |
| lossFactor               | Efficiency   |
| capacityPerSecondFrom    | Source field |
| enabledRequirements_json | Conditions   |

---

## Modifiers

General effect system.

| Column          | Description          |
| --------------- | -------------------- |
| id              | Modifier id          |
| source          | Upgrade/milestone id |
| zoneId          | Scope zone           |
| scope.\*        | Target scope         |
| operation       | multiply / add / set |
| target          | State path           |
| value           | Constant             |
| valueFromState  | State reference      |
| valueFromVar    | Computed var         |
| conditions_json | Optional conditions  |

---

## Upgrades

Purchasable effects.

| Column             | Description            |
| ------------------ | ---------------------- |
| id                 | Upgrade id             |
| displayName        | Name                   |
| presentation.\*    | Visual fields          |
| category           | manager / global / etc |
| zoneId             | Zone                   |
| cost_json          | Cost array             |
| repeatable         | bool                   |
| maxRank            | Max purchases          |
| rankCostScaling.\* | Optional scaling       |
| effects_json       | Modifier ids           |
| requirements_json  | Unlock rules           |
| tags               | Tags                   |

---

## Milestones

Level-based rewards.

| Column            | Description   |
| ----------------- | ------------- |
| id                | Milestone id  |
| presentation.\*   | Visuals       |
| nodeId            | Target node   |
| zoneId            | Zone          |
| atLevel           | Trigger level |
| grantEffects_json | Modifiers     |

---

## Projects

Timed actions.

| Column            | Description      |
| ----------------- | ---------------- |
| id                | Project id       |
| displayName       | Name             |
| zoneId            | Zone             |
| timeSeconds       | Duration         |
| cost_json         | Cost             |
| effects_json      | Rewards          |
| requirements_json | Conditions       |
| exclusiveGroup    | Mutual exclusion |
| tags              | Tags             |

---

## UnlockGraph

Progression rules.

| Column            | Description |
| ----------------- | ----------- |
| id                | Unlock id   |
| zoneId            | Zone        |
| unlocks_json      | Targets     |
| requirements_json | Conditions  |

---

## Buffs

Temporary modifiers.

| Column          | Description               |
| --------------- | ------------------------- |
| id              | Buff id                   |
| displayName     | Name                      |
| zoneId          | Zone                      |
| durationSeconds | Duration                  |
| stacking        | none / additive / replace |
| effects_json    | Modifiers                 |

---

## Prestige

Meta-progression configuration.

| Column            | Description     |
| ----------------- | --------------- |
| enabled           | bool            |
| zoneId            | Zone            |
| prestigeResource  | Meta currency   |
| formula.\*        | Calculation     |
| resetScopes.\*    | Reset rules     |
| metaUpgrades_json | Derived bonuses |

---

## Import Rules

- Empty rows are ignored
- Rows beginning with `_` are ignored
- Unknown columns are ignored
- Unknown sheets produce warnings but do not fail import
