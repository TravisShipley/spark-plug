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

## 5. Local API Key Setup

The Google Sheets API key is no longer stored on `GoogleSheetImportConfig` assets.

Create a local-only text file at:

- `Assets/Scripts/Content/Import/Editor/Local/google_sheets_api_key.txt`

Rules:

- put only the raw API key in the file
- do not add labels, JSON, or extra whitespace intentionally
- keep the file local; `Assets/Scripts/Content/Import/Editor/Local/` is ignored by git

Recommended setup:

1. create the folder `Assets/Scripts/Content/Import/Editor/Local/` if it does not exist
2. create `google_sheets_api_key.txt` inside that folder
3. paste the Google Sheets API key as the file contents
4. rerun the importer

If import fails with a missing key error, verify that the path and filename match exactly.

## 6. Output Location

Imported runtime definitions should be written under:

- `Assets/Data/Definitions/`

Examples:

- `Assets/Data/Definitions/proto_llama.json`
- `Assets/Data/Definitions/proto_orange.json`

The filename is an editor/import concern only. Runtime identity still comes from `GameSessionConfigAsset.sessionId`.

## 7. Import Profiles

Use one `GoogleSheetImportConfig` asset per generated definition when definitions need different output files or source sheets.

Recommended profile fields:

- `spreadsheetId`
- `outputJsonPath`
- optional `resourcesFallbackOutputPath`
- optional `addressableKey`

Recommended workflow:

1. Select a specific import profile and run `SparkPlug/Import Selected Definition`, or
2. Run `SparkPlug/Import All Definitions` when refreshing every generated definition

## 8. After Import

After a definition import completes:

1. assign the imported `TextAsset` to the relevant `GameSessionConfigAsset`
2. ensure the target scene is correct on that session config
3. verify the prototype still boots through `GameSessionBootstrapper`
