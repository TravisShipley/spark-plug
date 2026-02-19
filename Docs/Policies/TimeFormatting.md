---
document_role: policy
audience: ai, developers
scope: ui, formatting
status: active
---

# Time Formatting Policy

Spark Plug distinguishes between **countdown timers** and **informational durations**.  
These are **not interchangeable** and must use different formatting functions.

---

## 1. Countdown (Live Timers)

Use for UI elements that update in real time.

Examples:

- Generator progress remaining
- Buff remaining time (e.g., Ad Boost)
- Any value updated every frame or tick

**Requirements**

- Fixed-width format: `MM:SS`
- Use **ceiling** so the display never shows `00:00` early
- No unit labels (`m`, `s`, etc.)
- Format stability is required (no changing layouts)

**API**

```csharp
TimeFormat.FormatCountdown(double seconds)
```

---

## 2. Duration (Informational)

Use for static or descriptive time values.

Examples:

- Offline progress summary
- Project duration labels
- Tooltip text
- Buff duration description
- Content definitions

**Requirements**

- Human-readable format
- Adaptive units:
  - `45s`
  - `3m 12s`
  - `2h 5m`
- Format may change based on magnitude
- Not intended for per-frame updates

**API**

```csharp
TimeFormat.FormatDuration(long seconds)
```

---

## 3. Do Not Mix Usage

Avoid:

- Using `FormatDuration` in live countdown UI
- Using `FormatCountdown` for static labels
- Switching formats during a countdown
- Showing unit labels (`s`, `m`) in ticking timers

---

## 4. Architectural Rule

Time formatting is **presentation logic only**.

- Services must expose time as **seconds**
- Views/ViewModels choose the appropriate formatter
- Do not store formatted strings in SaveData or domain models

---

## 5. AI Guidance

When adding or modifying UI:

- If the value updates over time → **FormatCountdown**
- If the value is descriptive/static → **FormatDuration**
- If unsure, prefer **FormatCountdown** for anything player-wait related

Consistency is critical for perceived polish in idle game UI.
