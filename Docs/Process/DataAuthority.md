# Spark Plug – Data Authority

This document defines **where each type of data lives** and who is allowed to modify it.

Clear authority boundaries prevent corruption, duplication, and hidden state.

---

## 1. Content Data

| Data                                 | Authority                  | Location    | Mutated By              |
| ------------------------------------ | -------------------------- | ----------- | ----------------------- |
| Game content (nodes, upgrades, etc.) | Google Sheets              | External    | Designers / Sheet edits |
| Imported pack                        | JSON (`content_pack.json`) | Assets/Data | Importer only           |

Rules:

- JSON is a build artifact
- Never edit JSON manually
- Reimport from Sheets to change content

---

## 2. Player State

| Data               | Authority   | Location | Mutated By               |
| ------------------ | ----------- | -------- | ------------------------ |
| Wallet balances    | SaveService | GameData | Services via SaveService |
| Generator state    | SaveService | GameData | Services via SaveService |
| Purchased upgrades | SaveService | GameData | UpgradeService           |

Rules:

- Only `SaveService` writes to disk
- Services must call SaveService methods
- No other class accesses `SaveSystem`

---

## 3. Derived Values

Examples:

- Effective cycle duration
- Output multipliers
- Global bonuses

Authority: **Services**

Rules:

- Computed at runtime
- Not saved
- Recomputed on load

---

## 4. UI State

Examples:

- Progress animation
- Button visibility
- Visual timers

Authority: **Views**

Rules:

- Never affects simulation
- Never saved
- Must be reconstructible from Service state

---

## 5. Reset Behavior

Reset is achieved by:

1. Clearing SaveService data
2. Reloading the scene
3. Reinitializing services from content + defaults

---

## 6. Import Pipeline Authority

Flow:

```
Google Sheets
    ↓
Importer (Editor)
    ↓
content_pack.json
    ↓
PackLoaderService (runtime)
```

Runtime must treat content as **read-only**.

---

## 7. Principles

- Single source of truth for every piece of data
- Save facts, not derived values
- Runtime state must be reconstructible
- Fail loud if data is missing or invalid
