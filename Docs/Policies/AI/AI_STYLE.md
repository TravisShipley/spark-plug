---
document_role: policy
audience: ai, developers
scope: repository
status: active
---

# AI Style Contract

This document defines code style and architecture-aligned coding conventions for AI-authored changes.

## 1. Layer Responsibilities

- Services are authoritative and contain domain logic.
- ViewModels adapt service state for UI.
- Views render state and forward intent.
- Composition roots create and wire dependencies.

Do not move responsibilities across layers for convenience.

## 2. Naming

Use consistent type suffixes:

- `Service`: authoritative runtime systems
- `ViewModel`: UI adapters
- `View`: scene components
- `Definition`: imported data types
- `Catalog`: runtime content collections
- `CompositionRoot`: object graph wiring

Prefer explicit variable names for long-lived values.

## 3. Editing Hygiene

- Keep diffs minimal and localized.
- Do not rename public APIs unless requested.
- Do not reformat unrelated code.
- Avoid introducing new architectural patterns.

## 4. Data and Persistence Style

- Definitions are immutable at runtime.
- Persist facts only; recompute derived values.
- Keep persistence calls inside `SaveService` boundaries.

## 5. Unity-Specific Constraints

- Avoid scene searches (`FindObjectOfType`, `GameObject.Find`, etc.).
- Keep `Update` logic visual-only.

For hard scope limits, see `AI_SCOPE_BOUNDARIES.md`.
For task execution workflow, see `AI_TASK_STRATEGY.md`.
