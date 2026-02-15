---
document_role: design
topic: architecture
audience: ai, developers
scope: architecture
status: active
---

# Spark Plug – Simulation Model

This document defines authoritative generator runtime behavior.
For architectural constraints, see `ArchitectureRules.md`.

## 1. Generator Lifecycle

```text
Locked -> Owned -> Running -> Ready -> (Collect) -> Running
```

### Locked

- `IsOwned = false`
- No cycle progress accumulates

### Owned

- `IsOwned = true`
- First cycle starts immediately (`IsRunning = true`)

### Running

- Elapsed time increases via `TickService`
- Progress is computed from elapsed / cycle duration

### Ready

- `IsRunning = false`
- In manual mode: waits for player collect
- In automated mode: collects and restarts immediately

## 2. Timing Rules

- `CycleDurationSeconds` is authoritative and service-owned.
- Mid-cycle speed changes preserve percentage progress:

```text
percent = elapsed / oldDuration
elapsed = percent * newDuration
```

## 3. Output Rules

- Output is granted at collect time (manual or automated).
- Output derives from base output, level scaling, and active modifiers.

## 4. Automation Rules

- Manual mode stops at `Ready`.
- Automated mode collects and restarts continuously.
- Enabling automation applies immediately to subsequent behavior.

## 5. UI Interaction Contract

- Views may animate/interpolate progress visually.
- UI must not drive authoritative simulation timing/state.

## 6. Persistence Contract

Simulation saves facts only (ownership/level/automation/purchases), not transient timing state.

Related docs:

- `ServiceResponsibilities.md` (ownership)
- `../Process/DataAuthority.md` (fact vs derived storage)
