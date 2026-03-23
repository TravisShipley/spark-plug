---
document_role: process
topic: workflow
audience: ai, developers
scope: content
status: active
---

# Content Import Workflow

This document describes the operational sequence for importing spreadsheet content.

## 1. Authoring Prerequisites

- IDs are stable and unique.
- Enum-backed fields use `__Enums` values.
- `_json` fields are valid JSON.

## 2. Recommended Import Order

1. PackMeta
2. Resources
3. Phases
4. Zones
5. Nodes
6. NodeOutputs/NodeInputs/Scaling/Capacities
7. NodeInstances
8. Modifiers
9. Upgrades/Milestones/Buffs/Projects
10. UnlockGraph
11. Prestige

## 3. Validation Checks

- Referenced IDs exist before use.
- Modifier references resolve.
- Unlock targets exist.
- Unknown references fail import.

## 4. Safe Workflow

1. Edit sheets.
2. Choose the correct `GoogleSheetImportConfig` import profile for the target definition.
3. Import one profile or all profiles.
4. Fix validation errors.
5. Commit sheet + generated artifacts.

## 5. Output Location

Imported runtime definitions should be written under:

- `Assets/Data/Definitions/`

Examples:

- `Assets/Data/Definitions/proto_llama.json`
- `Assets/Data/Definitions/proto_orange.json`

The filename is an editor/import concern only. Runtime identity still comes from `GameSessionConfigAsset.sessionId`.

## 6. Import Profiles

Use one `GoogleSheetImportConfig` asset per generated definition when definitions need different output files or source sheets.

Recommended profile fields:

- `spreadsheetId`
- `apiKey`
- `outputJsonPath`
- optional `resourcesFallbackOutputPath`
- optional `addressableKey`

Recommended workflow:

1. Select a specific import profile and run `SparkPlug/Import Selected Definition`, or
2. Run `SparkPlug/Import All Definitions` when refreshing every generated definition

## 7. After Import

After a definition import completes:

1. assign the imported `TextAsset` to the relevant `GameSessionConfigAsset`
2. ensure the target scene is correct on that session config
3. verify the prototype still boots through `GameSessionBootstrapper`
