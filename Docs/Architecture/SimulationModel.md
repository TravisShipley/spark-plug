# Spark Plug – Simulation Model

This document defines the **authoritative runtime behavior** of generators and the core simulation loop.

The goal is to ensure that timing, upgrades, automation, and UI behavior remain consistent and predictable.

All simulation rules described here are implemented in **Services**, not in ViewModels or Views.

---

## 1. Simulation Authority

Simulation state lives in:

- `GeneratorService`
- `WalletService`
- `UpgradeService`
- `TickService`

Rules:

- Services own all timing and gameplay state
- UI never drives simulation timing
- ViewModels never compute simulation values
- Views never influence simulation directly

---

## 2. Generator Lifecycle

Each generator instance progresses through the following states:

```
Locked → Owned → Running → Ready → (Collect) → Running
```

### 2.1 Locked

- `IsOwned = false`
- No simulation occurs
- Build action transitions to Owned

---

### 2.2 Owned

- `IsOwned = true`
- Generator immediately begins its first cycle
- `IsRunning = true`

---

### 2.3 Running

- Elapsed time accumulates via `TickService`
- Progress percentage:

```
progress = elapsedTime / CycleDurationSeconds
```

When:

```
elapsedTime >= CycleDurationSeconds
```

Transition to **Ready**

---

### 2.4 Ready

Authoritative state:

- `IsRunning = false`
- `IsReadyForCollect = true`
- Progress is logically **1.0**

Behavior depends on automation.

#### Manual mode

- Generator waits for player action
- No further time accumulation

#### Automated mode

Immediately:

1. Output is collected
2. Elapsed time resets to 0
3. State returns to Running

---

### 2.5 Collect (Manual)

When player collects:

1. Output is granted via `WalletService`
2. Elapsed time resets to 0
3. `IsReadyForCollect = false`
4. `IsRunning = true`

---

## 3. Cycle Timing (Authoritative)

`GeneratorService` exposes:

- `CycleDurationSeconds` (authoritative)
- `OutputPerCycle`
- `IsRunning`
- `IsReadyForCollect`

Cycle duration is calculated from:

```
BaseDuration
× Level modifiers
× Upgrade multipliers
× Global multipliers
```

This value is:

- Computed in `GeneratorService`
- Exposed reactively
- Never calculated in ViewModel or View

---

## 4. Mid-Cycle Changes

When cycle duration changes while running:

### Rule

Progress is preserved as **percentage**, not elapsed seconds.

```
percent = elapsedTime / oldDuration
elapsedTime = percent × newDuration
```

This ensures:

- No visible jump backward
- Remaining time adjusts correctly

---

## 5. Automation Behavior

Automation controls two behaviors:

| Mode      | Behavior                   |
| --------- | -------------------------- |
| Manual    | Stops at Ready             |
| Automated | Auto-collects and restarts |

Automation state is authoritative:

```
IsAutomated
```

Changes to automation:

- Take effect immediately
- If enabled while Ready → collect immediately
- If enabled mid-cycle → next Ready auto-collects

---

## 6. Output Calculation

Output is determined at **collection time**:

```
OutputPerCycle
× Level scaling
× Upgrade multipliers
× Global modifiers
```

Rules:

- Output is not accumulated continuously
- Only granted on collect (manual or auto)

---

## 7. Tick Model

`TickService` provides:

- Delta time
- Consistent simulation updates

Rules:

- No gameplay logic inside TickService
- Services subscribe and update their own state

---

## 8. UI Model (Decoupled)

UI progress bars are **visual only**.

Views may:

- Animate progress smoothly
- Continue animating after Ready
- Interpolate between values

Views must NOT:

- Drive simulation timing
- Clamp or alter service state
- Assume cycle duration without reading from Service

Authoritative state comes from:

- `IsRunning`
- `IsReadyForCollect`
- `CycleDurationSeconds`

---

## 9. Persistence Model

Saved facts:

- Owned state
- Level
- Automation state

Not saved:

- Elapsed time
- Progress percentage
- Derived durations
- Multipliers

On load:

- Generators start fresh cycles

---

## 10. Determinism Goals

The simulation should be:

- Frame-rate independent
- Stable with multiple generators
- Safe for high speeds (sub-second cycles)
- Free of per-frame allocations

---

## 11. Common Failure Modes

❌ Calculating duration in ViewModel  
❌ Resetting progress visually before service state changes  
❌ Losing progress on speed upgrades  
❌ Saving elapsed time  
❌ Using UI timers as simulation source

---

## 12. Design Principles

- Services are authoritative
- UI is cosmetic
- Progress is percentage-based
- Save facts only
- Simulation must be reconstructible from content + save
