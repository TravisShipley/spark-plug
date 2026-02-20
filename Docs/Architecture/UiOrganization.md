---
document_role: policy
topic: architecture
audience: developers, designers
scope: system:ui
status: active
---

# Spark Plug - UI Naming & Organization Dos/Don’ts

This project groups UI code **by feature** (folders) and uses consistent naming so:

- files are easy to find
- view ↔ viewmodel pairing is obvious
- analytics screen names stay stable
- engine terminology doesn’t leak into player-facing concepts

---

## Do

### ✅ Group UI by feature folder

Use folders that match the screen taxonomy / user intent:

```
UI/Home
UI/Nodes
UI/Upgrades
UI/Automation
UI/Boost
UI/OfflineEarnings
UI/Prestige
UI/Settings
UI/Store
UI/Projects
UI/Event
```

`Nodes` is acceptable as a neutral internal term.

---

### ✅ Use `Screen` suffix only for navigable screens

- `BoostScreenView`
- `UpgradesScreenView`
- `PrestigeScreenView`

Embedded pieces are not screens:

- `NodeTileView`
- `UpgradeEntryView`

---

### ✅ Pair View and ViewModel by exact stem

Naming must match exactly:

- `BoostScreenView` ↔ `BoostScreenViewModel`
- `NodeTileView` ↔ `NodeTileViewModel`

This makes search and refactors trivial.

---

### ✅ Put shared UI utilities in `UI/_Shared/`

Only for items used by **2+** features:

```
UI/_Shared/Binding/
UI/_Shared/Formatting/
UI/_Shared/Navigation/
UI/_Shared/Components/
```

Examples:

- binders (TextBinder, ActiveBinder)
- number formatting
- screen navigation service
- reusable components (progress bar)

---

### ✅ Use verbs for commands, nouns for views

Commands:

- `OpenBoostCommand`
- `BuyLevelCommand`
- `CollectCommand`
- `ActivateBoostCommand`

Views/Components:

- `CurrencyTextView`
- `ProgressBarView`
- `NodeTileView`

---

### ✅ Keep UI logic “presentation-only”

Allowed in ViewModels:

- derived strings
- formatting
- sorting/filtering
- mapping state → UI properties

Allowed in Views:

- Unity lifecycle wiring (`Awake`, `OnEnable`)
- subscriptions/bindings
- button hookups

Gameplay logic belongs in Domain services:

- purchasing rules
- modifier math
- unlock checks
- state mutations

---

### ✅ Analytics screen names are stable and intent-based

Screen view events should use the screen stem:

- `BoostScreenView` logs `screen_view: Boost`
- `UpgradesScreenView` logs `screen_view: Upgrades`

Details go in parameters, not screen names:

- `boostId`
- `zoneId`
- `nodeId`

---

## Don’t

### ❌ Don’t make an `MVVM/` folder

MVVM is a pattern, not a directory requirement.
We group by feature.

---

### ❌ Don’t put ViewModels in Domain

No:

- `Domain/**/SomethingViewModel.cs`

ViewModels live in the UI feature folder they serve.

---

### ❌ Don’t encode variants in screen names

Avoid:

- `ProfitBoostScreen`
- `HalloweenEventScreen`
- `AppleUpgradesScreen`

Use a stable screen + parameters:

- `screen: Boost`, `boostId: buff.adBoost.profit2x`
- `screen: Event`, `eventId: event.halloween.2026`

---

### ❌ Don’t dump one-off widgets into `_Shared`

If only used by one feature, keep it local to that folder.

Avoid a “junk drawer” `_Shared`.

---

### ❌ Don’t use ambiguous suffixes

Avoid:

- `Manager.cs` (too vague)
- `Helper.cs` (usually unclear)
- `Util.cs` (tends to grow forever)

Prefer explicit roles:

- `*Service`
- `*Catalog`
- `*Formatter`
- `*Binder`
- `*Controller` (optional)
- `*Command`

---

### ❌ Don’t let engine terms drive player-facing UI names

Internal folder names can be neutral, but avoid exposing engine jargon in:

- UI copy
- screen analytics names
- player-visible labels

Examples to avoid in player-facing contexts:

- `Modifier`
- `ComputedVar`
- `NodeType`
- `Buff` (prefer `Boost`)

---

## Quick Examples

### Boost feature

```
UI/Boost/
  BoostScreenView.cs
  BoostScreenViewModel.cs
  BoostButtonView.cs
  BoostButtonViewModel.cs
  ActivateBoostCommand.cs
  OpenBoostCommand.cs
```

### Nodes feature

```
UI/Nodes/
  NodeTileView.cs
  NodeTileViewModel.cs
  NodePurchaseControlsView.cs
  NodePurchaseControlsViewModel.cs
```

---

## Rule of Thumb

If you can’t guess the file’s folder from its name, the name is wrong.

- Navigable? → `*ScreenView`
- Reusable component? → `*View`
- User action? → `*Command`
- Formatting? → `*Formatter`
- Binding glue? → `*Binder`
