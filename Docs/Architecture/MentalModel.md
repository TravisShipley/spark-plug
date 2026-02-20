---
document_role: design
topic: systems
audience: designers, engineers
scope: gameplay
status: active
---

# Spark Plug Mental Model

Spark Plug is built around one core idea:

> **Everything that changes the game is a Modifier.**  
> All other systems only **activate** modifiers.

If you understand this, the entire schema becomes predictable.

---

# The Big Picture

```
Player Action / Progress
        ↓
Upgrade / Milestone / Buff / Project / Prestige
        ↓
      Modifiers
        ↓
 Runtime State Changes
        ↓
 Production / Economy Output
```

---

# The Core Layers

## 1) Content (Definitions)

Static data authored in Sheets:

- Nodes
- Resources
- Upgrades
- Milestones
- Buffs
- Projects
- UnlockGraph
- Modifiers

These describe **what can happen**.

---

## 2) Runtime State

What the player currently has:

- Resource amounts
- Node levels
- Active buffs
- Purchased upgrades
- Completed projects
- Current phase
- Zone progress

---

## 3) Systems

Systems evaluate content + runtime:

| System          | What it does              |
| --------------- | ------------------------- |
| Node System     | Produces resources        |
| Modifier System | Applies effects           |
| Unlock System   | Enables content           |
| Buff System     | Handles temporary effects |
| Prestige System | Handles resets            |

---

# The Economy Loop

## Node Production

```
Node Level
    ↓
Base Output
    ↓
Apply Modifiers
    ↓
Final Output
    ↓
Add to Resources
```

Modifiers affect:

- Output
- Speed
- Capacity
- Costs
- Global multipliers

---

# Modifiers (The Center of Everything)

A modifier answers:

| Question      | Field     |
| ------------- | --------- |
| What changes? | target    |
| Where?        | scope     |
| How?          | operation |
| How much?     | value     |
| Why?          | source    |

Example:

```
source: buff.adBoost.profit2x
scope: zone.main
target: nodeOutput[currencySoft]
operation: multiply
value: 2
```

---

# Target vs Scope (Important)

**Scope = WHERE**

```
global
zone
node
nodeTag
resource
```

**Target = WHAT**

```
nodeOutput[currencySoft]
nodeSpeedMultiplier
resourceGain[currencySoft]
automation.policy
```

Rule:

> Scope selects entities  
> Target selects the property

---

# Bracket Form

When a target depends on a resource:

```
nodeOutput[currencySoft]
resourceGain[currencySoft]
resource[currencyMeta]
```

Meaning:

```
baseTarget[parameter]
```

---

# How Systems Activate Modifiers

| System      | Activation          |
| ----------- | ------------------- |
| Upgrade     | Purchased           |
| Milestone   | Level reached       |
| Buff        | Time-based          |
| Project     | Completed           |
| Prestige    | Meta purchase       |
| UnlockGraph | Progress conditions |

They all just add modifiers to the active set.

---

# Progression Model

## Level → Milestone → Modifier

```
Node Level increases
        ↓
Milestone reached
        ↓
grantEffects → modifierIds
```

MilestoneRank = number of milestones reached (derived)

---

# Unlock Model

```
Requirements met
        ↓
UnlockGraph entry activates
        ↓
Content becomes available
```

Unlocks can enable:

- Nodes
- NodeInstances
- Upgrades
- Projects
- Zones
- Phases

---

# Buff Model

```
Buff activated
        ↓
effects → modifierIds
        ↓
Modifier active for duration
        ↓
Removed when expired
```

Stacking behavior:

- refresh
- extend
- ignore
- stack

---

# Prestige Model

```
Prestige triggered
        ↓
ResetScopes applied
        ↓
Meta currency granted
        ↓
MetaUpgrades add permanent modifiers
```

---

# Authoring Mental Model

When designing a feature, ask:

### Step 1 — What changes?

→ Create a **Modifier**

### Step 2 — When does it happen?

→ Reference modifier from:

- Upgrade
- Milestone
- Buff
- Project
- UnlockGraph
- Prestige

### Step 3 — Where does it apply?

→ Set **scope**

---

# Example: Rewarded Ad

**Buff**

```
buff.adBoost.profit2x
```

**Modifier**

```
target: nodeOutput[currencySoft]
operation: multiply
value: 2
scope: zone.main
```

Flow:

```
Watch Ad → Buff → Modifier → Production ×2
```

---

# Example: Unlock Banana at Apple Level 50

```
UnlockGraph:
requirement: nodeLevelAtLeast apple 50
unlocks: nodeInstance.banana
```

---

# Example: Apple Level 25 Bonus

```
Milestone.apple.25
grantEffects → modifier.milestone.apple.25.output
```

---

# Design Rules (Important)

1. **All gameplay math lives in Modifiers**
2. Other systems only trigger modifiers
3. Avoid hardcoding behavior outside the modifier system
4. Prefer scope over creating new target types
5. Use bracket form for resource-specific targets

---

# If Something Feels Complex…

Ask:

- Is this just a modifier?
- Am I mixing scope and target?
- Should this be triggered by UnlockGraph instead?

Most Spark Plug problems are solved by:
→ **One modifier + correct activation**

---

# One-Sentence Summary

> Spark Plug is a data-driven engine where progression systems activate modifiers that shape a single unified economy.
