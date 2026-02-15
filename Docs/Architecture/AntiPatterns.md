---
document_role: policy
topic: architecture
audience: ai, developers
scope: architecture
status: active
---

# Spark Plug – Architectural Anti-Patterns

This page is a quick "do not do this" checklist.
Canonical rules live in `ArchitectureRules.md`.

## 1. UI Owns Gameplay State

- Gameplay logic in `MonoBehaviour`
- Views mutating domain state directly
- ViewModels computing authoritative simulation values

## 2. Runtime Dependency Discovery

- `FindObjectOfType`, `FindAnyObjectByType`, `GameObject.Find`
- Tag-based dependency lookups

## 3. Multiple Persistence Paths

- Any class calling `SaveSystem` except `SaveService`
- Saving derived values as facts
- Writing to disk from multiple subsystems

## 4. Hidden or Implicit State

- Static singletons as hidden authority
- Globals that bypass composition roots
- State ownership without a named authority

## 5. Silent Data Failure

- Ignoring missing references
- Defaulting invalid content silently
- Continuing execution after critical validation failures

Related docs:

- `ArchitectureRules.md` (authoritative constraints)
- `ServiceResponsibilities.md` (ownership map)
- `SimulationModel.md` (generator timing behavior)
