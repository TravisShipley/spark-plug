# Smoke Test: Vertical Slice (2-3 Minutes)

## Purpose
Quickly verify the core loop still works end-to-end after changes:
- fresh start/reset
- earn currency
- level a node
- buy an upgrade
- enable automation (manager) if available
- trigger milestone effects
- unlock progression check
- reload and verify persistence

## Content Pack Assumptions
This checklist is written for `Assets/Data/game_definition.json`:
- zone: `zone.main`
- node instances:
  - `nodeInstance.producer.apple`
  - `nodeInstance.producer.grape`
  - `nodeInstance.producer.banana`
- sample upgrades:
  - `upgrade.node.apple.001`
  - `upgrade.automation.apple`
- sample milestones:
  - `milestone.apple.25`

## Reset To Fresh State
Use one of these existing reset paths before each smoke run:

1. In play mode, press the in-game reset/clear-save flow (if present in your scene).
2. In editor menu, use `SparkPlug/Smoke Test/Reset Save` (if `SparkPlugSmokeTestRunner` is present).

Expected outcome:
- next play start behaves like a brand-new run from content defaults
- only `nodeInstance.producer.apple` starts enabled
- grape/banana are not enabled unless your pack/config unlocks them

## 2-3 Minute Checklist

1. Start from fresh state and enter play mode.
Expected:
- wallet starts near zero soft currency
- Apple generator row is visible/usable
- Grape/Banana start locked or disabled unless your pack says otherwise

2. Earn currency by running/collecting Apple for a few cycles.
Expected:
- `currencySoft` increases after each collect
- no errors in console

3. Buy at least 1 Apple level (`nodeInstance.producer.apple`).
Expected:
- Apple level increases
- next level cost increases
- per-cycle output increases

4. Buy one node upgrade (recommended `upgrade.node.apple.001`).
Expected:
- upgrade is marked purchased/unavailable for repurchase (if one-time)
- Apple cycle/output behavior changes immediately (speed/output per modifier)

5. Buy automation/manager if available (recommended `upgrade.automation.apple`).
Expected:
- Apple auto-runs/auto-collect behavior is enabled for this slice implementation
- automation state persists as purchased

6. Reach first milestone threshold (`milestone.apple.25`).
Expected:
- milestone effect applies when level threshold is reached
- milestone should not re-fire repeatedly once fired

7. Unlock progression check (UnlockGraph).
Expected:
- if your current pack has unlock graph entries, the next target node instance unlocks when requirements are met
- if your current pack has `unlockGraph: []` (current default file), treat this as N/A and verify existing enabled set remains stable

8. Exit play mode, re-enter play mode (or restart app/domain).
Expected persisted facts:
- resource balances restored
- node levels restored
- purchased upgrades restored
- unlocked node instances restored
- automation purchased state restored
- fired milestones remain fired (no duplicate one-time milestone fire)

## Quick Verification Aid
Use `SparkPlug/Smoke Test/Print Current State` to print one grouped snapshot:
- current zone
- node instance levels/owned/enabled/automation
- purchased upgrades
- unlocked node instances
- fired milestones
- milestone rank by node instance

