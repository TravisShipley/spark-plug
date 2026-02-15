---
document_role: design
audience: ai, developers
scope: architecture
status: draft
---

# Spark Plug – Display Data Pipeline (Future Architecture)

**Status:** Planned / Not yet implemented  
**Audience:** Engineers, Tech Design, Live Ops  
**Scope:** How display data (names, icons, prefabs, etc.) should scale to a multi-team live-ops environment

---

## Overview

As Spark Plug scales to support multiple games and live operations, display data must be separated from gameplay logic and managed through a robust, automated pipeline.

The long-term goal is:

> **Sheets as the source of truth → automated generation → Unity Addressables + Localization + optional ScriptableObjects**

This allows large teams to safely update content without manual Unity wiring or full app releases.

---

## Design Goals

- Separate **gameplay logic** from **display**
- Allow non-engineers to author content safely
- Minimize manual Unity setup
- Support **live ops content releases**
- Support **multiple games** using the same engine
- Enable CI validation and automated builds
- Prevent broken references in production

---

## High-Level Architecture

### Gameplay Pipeline (existing)

Sheets → Importer → `gameplay_pack.json`

Contains:

- Nodes
- Upgrades
- Modifiers
- Economy data
- Unlock logic
- Curves / formulas

**No Unity asset references**

---

### Display Data Pipeline (future)

Sheets → Generator → Unity Assets

Generator produces:

1. Localization entries
2. Addressables configuration
3. Optional Display ScriptableObjects
4. Validation reports

Runtime loads:

- Gameplay pack (JSON)
- Display assets (Addressables)

Joined by **stable IDs**.

---

## Source of Truth: Display Sheets

Display should live in separate tabs or a separate spreadsheet.

### Example: `NodeDisplay`

| nodeId                  | nameKey            | descKey            | iconAddress       | prefabAddress           | sort | category |
| ----------------------- | ------------------ | ------------------ | ----------------- | ----------------------- | ---: | -------- |
| nodeType.producer.apple | ui.node.apple.name | ui.node.apple.desc | icons/nodes/apple | prefabs/nodes/appleCard |  100 | fruit    |

Guidelines:

- `nodeId` must match gameplay pack
- Text is stored as **localization keys**, not raw strings
- Addresses follow conventions where possible
- UI-only metadata is allowed (sort, category, grouping)

Similar tables should exist for:

- UpgradeDisplay
- ResourceDisplay
- ZoneDisplay
- (Optional) EventDisplay

---

## Localization Generation

The generator should:

- Create/update `StringTableCollection`
- Add missing keys
- Update values from Sheets
- Validate required locales

Optional sheet structure:

| key                | en         | fr              | de        |
| ------------------ | ---------- | --------------- | --------- |
| ui.node.apple.name | Apple Farm | Ferme de pommes | Apfelfarm |

CI should fail if required locales are missing.

---

## Addressables Generation

The generator should:

- Ensure assets exist at specified paths
- Mark assets as Addressable
- Assign:
  - Address
  - Group
  - Labels

### Labeling Convention

| Label                 | Purpose             |
| --------------------- | ------------------- |
| gameA / gameB / gameC | Game ownership      |
| shared                | Shared across games |
| ui                    | UI assets           |
| event.halloween2026   | Live ops event      |

This enables:

- Per-game catalogs
- Event-only releases
- Shared asset reuse

---

## Convention-First Addressing (Recommended)

Avoid free-text addresses when possible.

Example rules:

```
iconAddress = icons/nodes/{shortId}
prefabAddress = prefabs/nodes/{shortId}Card
```

Where:

```
shortId = apple   // derived from nodeType.producer.apple
```

Sheets should only provide overrides when necessary.

Benefits:

- Fewer typos
- Consistent structure
- Easier automation

---

## Optional: Generated Display ScriptableObjects

For Unity workflows, the generator may create assets such as:

### `NodeDisplayAsset`

Fields:

- nodeId
- LocalizedString name
- LocalizedString description
- AssetReferenceSprite icon
- AssetReferenceGameObject prefab
- int sortOrder
- string category

These assets should also be Addressable.

Use ScriptableObjects when:

- Unity inspector workflows are valuable
- Multiple asset references must be grouped
- Debugging/QA benefits from clickable assets

---

## Runtime Model

Runtime loads:

1. Gameplay pack (JSON)
2. Display assets (Addressables)

Join example:

```
node.nodeId == NodeDisplayAsset.nodeId
```

UI reads display data only.

Gameplay systems never reference Unity assets directly.

---

## Validation (Required)

CI should validate:

### Gameplay

- Schema validity
- Reference integrity

### Display

- Every gameplay ID has display
- All addresses exist
- Addressables entries exist
- Localization keys exist
- Required locales present
- No duplicate IDs or addresses

Build should fail on validation errors.

---

## Multi-Game Considerations

This pipeline supports:

- Multiple games using the same engine
- Shared content libraries
- Game-specific themes
- Game-specific catalogs

Recommended structure:

```
Assets/GameA/...
Assets/GameB/...
Assets/Shared/...
```

Labels applied automatically.

---

## Live Ops Workflow

Typical content release:

1. Update Sheets
2. Run generator
3. CI validation
4. Build Addressables content
5. Publish catalog
6. Client downloads update

No full app release required (within Addressables constraints).

---

## Generator Requirements

The generator must be:

- **Idempotent** (safe to run repeatedly)
- Deterministic
- Scriptable via CLI / batchmode
- Integrated into CI

Optional features:

- Placeholder asset creation
- Missing asset reports
- Diff summaries

---

## Implementation Phases

### Phase 1 (Current)

- Gameplay in Sheets
- Display mixed or minimal

---

### Phase 2 (Recommended Next Step)

- Separate Display sheets
- Use keys instead of raw text
- No generator yet

---

### Phase 3 (Full Pipeline)

- Implement generator
- Addressables automation
- Localization generation
- CI validation

---

## What Does NOT Belong in Gameplay Sheets

Do not include:

- Unity asset references
- Sprite paths
- Prefab references
- Localization text
- UI layout details

Gameplay packs must remain engine-pure.

---

## Future Extensions

- Event content packs
- Theme/skin swapping
- A/B test display variants
- Content authoring tools
- Web-based preview tools

---

## Summary

This pipeline enables:

- Safe scaling to large teams
- Clean separation of concerns
- Automated content integration
- Reliable live ops
- Multi-game support

It should be implemented before large-scale live content production begins.
