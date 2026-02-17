---
document_role: policy
topic: architecture
audience: ai, developers
scope: repository
status: active
---

# Spark Plug – Coding Standards

These rules apply to all runtime and editor code.

The goal is clarity, long-term maintainability, and predictable AI-generated code.

---

## 1. Prefer explicit variable names

Avoid abbreviated or single-letter variable names except for trivial loop indices.

**Good**

```csharp
var graphic = new Graphic();
var generatorService = new GeneratorService();
var cycleDurationSeconds = service.CycleDurationSeconds;
```

**Avoid**

```csharp
var g = new Graphic();
var svc = new GeneratorService();
var d = service.CycleDurationSeconds;
```

If a variable lives longer than a few lines, its purpose should be obvious without reading surrounding code.

---

## 2. Avoid ambiguous abbreviations

Do not use:

- `svc`
- `mgr`
- `ctx`
- `cfg`
- `vm` (unless in View-specific code)

Prefer full words:

- `service`
- `manager`
- `context`
- `config`
- `viewModel`

---

## 3. Optimize for “readable in six months”

When choosing between:

- shorter code
- clearer code

**Always choose clarity.**

---

## 4. Prefer brevity when the context is obvious

Explicit names are preferred, but avoid unnecessary verbosity when the meaning is already clear from context.

### 4.1 Short loop variables

Single-letter names are acceptable for small, local loops.

**Good**

```csharp
for (int i = 0; i < generators.Count; i++)
{
    generators[i].Tick(deltaTime);
}
```

```csharp
foreach (var node in nodes)
{
    node.Initialize();
}
```

**Avoid**

```csharp
for (int generatorIndex = 0; generatorIndex < generators.Count; generatorIndex++)
```

If the variable is used only within a small block, brevity improves readability.

---

### 4.2 LINQ and short-lived lambdas

Short names are preferred inside LINQ expressions.

**Good**

```csharp
var running = generators.Where(g => g.IsRunning);
var total = generators.Sum(g => g.OutputPerSecond);
```

**Avoid**

```csharp
var running = generators.Where(generatorService => generatorService.IsRunning);
```

---

### 4.3 Obvious type context

Avoid repeating the type name when the variable’s role is clear.

**Good**

```csharp
var wallet = new WalletService();
var screen = uiScreenManager.Show<UpgradesScreenView>();
```

**Avoid**

```csharp
var walletServiceInstance = new WalletService();
var upgradesScreenViewInstance = uiScreenManager.Show<UpgradesScreenView>();
```

---

### 4.4 Method-local temporaries

If a variable exists only to clarify a calculation, keep it short but meaningful.

**Good**

```csharp
var dt = Time.deltaTime;
elapsed += dt;
```

**Avoid**

```csharp
var deltaTimeSinceLastFrame = Time.deltaTime;
```

---

## Guideline

- **Long-lived variables** → explicit names
- **Short-lived or local variables** → concise names
- Prefer the shortest name that is still clear in its immediate context

When in doubt, optimize for **scan readability**, not word count.

---

## 5. Avoid semantic noise words

Avoid adding generic suffixes that do not add meaning.

Do not use words like:

- `instance`
- `data`
- `object`
- `item`
- `value`
- `info`
- `thing`

**Good**

```csharp
var wallet = new WalletService();
var generator = generators[i];
var definition = nodeCatalog.Get(id);
```

**Avoid**

```csharp
var walletServiceInstance = new WalletService();
var generatorItem = generators[i];
var nodeDefinitionData = nodeCatalog.Get(id);
```

Use names that describe the role, not the fact that the variable exists.

---

### Exception

Noise words are acceptable only when they distinguish between two different concepts.

**Example**

```csharp
var saveData = saveService.Data;
var runtimeState = generator.State;
```

If the word helps clarify a domain distinction, it may be used.

---

## 6. Enforce MVVM boundaries

UI code must follow this direction of responsibility:

`Domain/Service -> ViewModel -> View`

### 6.1 Service / Domain responsibilities

- Own gameplay/business rules and derived state.
- Expose reactive state needed by UI (`IReadOnlyReactiveProperty<T>`, observables, or equivalent).
- Handle calculations like progression ratios, thresholds, affordability, unlock logic, and modifier effects.
- Never read Unity UI components directly.

### 6.2 ViewModel responsibilities

- Adapt service/domain state for presentation.
- Compose multiple streams when needed for display semantics.
- Expose current values plus change streams.
- Do not become source-of-truth for gameplay state.

### 6.3 View responsibilities

- Bind to ViewModel outputs and render.
- Handle visual-only formatting and presentation concerns.
- Forward user input/intents to services through ViewModel/service APIs.
- Must not contain gameplay or progression calculations.

### 6.4 Forbidden in Views

- Derived gameplay math (ratios, rank progression, thresholds, unlock checks).
- Content-definition lookups (catalog/definition traversal).
- Save-state mutation.
- Modifier resolution logic.

If a view needs a value that is not exposed yet, add it to service/domain (preferred) or ViewModel composition layer.

### 6.5 UI change review checklist (required)

For any UI-related PR/change, include and answer:

- [ ] Where does truth live? (service/domain type + field/property)
- [ ] What is derived vs persisted?
- [ ] Does the view only bind/render?
- [ ] Is any gameplay math still in the view?
- [ ] Is data flow explicitly documented?

### 6.6 Required data-flow note in PR/AI output

Include a short section:

```text
Data Flow:
<domain source> -> <viewmodel property> -> <view binding>
```

Example:

```text
Data Flow:
GeneratorService.MilestoneProgressRatio
-> GeneratorViewModel.MilestoneProgressRatio
-> GeneratorView.levelProgressFill.fillAmount
```
