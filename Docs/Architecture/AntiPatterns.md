---
document_role: policy
audience: ai, developers
scope: architecture
status: active
---

# Spark Plug – Architectural Anti-Patterns

These patterns are explicitly **forbidden** because they lead to hidden state, tight coupling, or long-term instability.

---

## 1. Logic in Views

Forbidden:

- Gameplay logic in `MonoBehaviour`
- State mutation from UI
- Services accessed directly from Views

Views should only:

- Bind
- Display
- Forward user intent

---

## 2. Authoritative Logic in ViewModels

ViewModels must not:

- Compute game rules
- Store independent state
- Derive values that should come from Services

If a value affects simulation, it belongs in a Service.

---

## 3. Scene Searching

Forbidden:

- `FindObjectOfType`
- `FindAnyObjectByType`
- Tag-based lookups

All dependencies must be injected via CompositionRoots.

---

## 4. Service-to-Service Coupling

Services should not directly depend on other Services unless explicitly composed.

If coordination is needed:

- Use CompositionRoot
- Or create a dedicated orchestration service

---

## 5. Multiple Persistence Paths

Forbidden:

- Calling `SaveSystem` outside `SaveService`
- Writing to disk from multiple locations
- Saving derived values

---

## 6. Silent Fallbacks

Forbidden:

- Ignoring missing references
- Defaulting silently when data is invalid

Always log or throw clear errors.

---

## 7. One-Off UI Classes

Avoid:

- Custom class per button
- Duplicated binding logic

Prefer reusable binders and `UiCommand`.

---

## 8. Magic Numbers

Replace:

```
progress >= 0.9999f
```

With named constants expressing intent.

---

## 9. Hidden State

Avoid:

- Static singletons
- Implicit globals
- State stored outside Services without clear authority
