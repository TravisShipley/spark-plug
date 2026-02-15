---
document_role: design
topic: architecture
audience: ai, developers
scope: architecture
status: active
---

# Spark Plug Overview

Spark Plug is a data-driven idle game engine built around explicit authority boundaries.

## Purpose

This document is the high-level orientation page. It summarizes the runtime model and points to detailed documents.

## Runtime Shape

```text
GameDefinition -> Services -> ViewModels -> Views
```

- Content defines behavior.
- Services execute simulation.
- ViewModels adapt state.
- Views render and forward intent.

## Core Components

- `ArchitectureRules.md`: normative architecture and naming policy.
- `SystemMap.md`: runtime graph and data flow map.
- `ServiceResponsibilities.md`: service ownership boundaries.
- `SimulationModel.md`: authoritative generator timing model.

## Data and Persistence at a Glance

- Content is imported from Sheets into runtime JSON.
- Player facts are persisted.
- Derived values are recomputed on load.

For strict rules, use `ArchitectureRules.md` as source of truth.
