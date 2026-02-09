# Contributing to Spark Plug

This project follows strict architectural and structural rules.  
These guidelines apply to **both humans and AI assistants**.

The primary goal is long-term maintainability and clarity.

---

## 1. Core Principles

- Fail loud over silent fallback
- Authoritative state lives in **Services**
- UI is dumb and reactive
- Prefer explicit wiring over discovery
- Optimize for readability six months later

---

## 2. Architecture Rules

### 2.1 Dependency Direction

Allowed:

```
View → ViewModel → Service → SaveService
```

Forbidden:

- Services referencing Views or ViewModels
- Views accessing Services directly
- Any class calling `SaveSystem` except `SaveService`
- Scene searches (`FindObjectOfType`, etc.)

---

### 2.2 Service Rules

Services:

- Pure C# (no `MonoBehaviour`)
- Own domain state and rules
- Are constructed only in a CompositionRoot
- Do not reference UI or Unity objects
- Trigger persistence only through `SaveService`

---

### 2.3 ViewModel Rules

ViewModels:

- Expose `IObservable` / `IReadOnlyReactiveProperty`
- Do not contain authoritative state
- Do not compute domain logic
- Forward values from Services

---

### 2.4 View Rules

Views:

- `MonoBehaviour` only
- No gameplay logic
- No state mutation
- Bind once via `Bind(ViewModel)`
- No scene searches

---

## 3. Composition

Only CompositionRoots may:

- Instantiate Services
- Instantiate ViewModels
- Wire dependencies
- Load save state

Construction and loading must be separate phases.

---

## 4. Persistence

`SaveService` is the **single authority**.

Rules:

- Save **facts**, not derived values
- Services mutate state via `SaveService`
- Disk IO is debounced and internal to `SaveService`
- Scene reload is used for full reset

---

## 5. Code Structure

- One class per file
- File name matches class name
- Explicit names (no abbreviations)
- Avoid magic numbers
- Prefer additive changes over refactors

---

## 6. Data Pipeline

Content authority:

| Source        | Authority       |
| ------------- | --------------- |
| Google Sheets | Game content    |
| JSON pack     | Import artifact |
| SaveService   | Player state    |

Do not edit generated JSON manually.

---

## 7. Before Submitting Changes

Verify:

- Project compiles cleanly
- No new scene searches
- No UI logic in Services
- No direct disk access outside `SaveService`
- Existing vertical slice still works

---

## 8. AI-Specific Guidance

AI tools should:

- Read `Docs/Architecture` first
- Prefer minimal, localized changes
- Never rename public APIs without listing call sites
- Never move authoritative logic into ViewModels
- Avoid creating one-off classes when reusable patterns exist
