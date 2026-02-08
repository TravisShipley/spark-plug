# Spark Plug – \_\_Enums Reference

The `__Enums` sheet defines controlled string values used across the content spreadsheet.

This sheet exists to:

- Prevent typos
- Enable dropdown validation
- Provide a single source of truth for string-based configuration
- Allow the importer to validate content

---

## Structure

| Column      | Description                         |
| ----------- | ----------------------------------- |
| enum        | Enum group name                     |
| value       | Allowed value                       |
| description | Optional human-readable explanation |

Example:

| enum         | value        |
| ------------ | ------------ |
| ResourceKind | softCurrency |
| ResourceKind | hardCurrency |
| ResourceKind | metaCurrency |

---

## Usage

Dropdowns should reference values derived from `__Enums`.

Importer validation should fail if a value is not listed for its enum.

---

## Recommended Enum Groups

### ResourceKind

- softCurrency
- hardCurrency
- metaCurrency

---

### NumberFormatType

- mantissaExponent
- suffix

---

### FormatStyle

- currency
- suffix
- plain
- percent

---

### NodeType

- producer
- converter
- buffer

---

### OutputMode

- cycle
- perSecond

---

### AutomationPolicy

- manualCollect
- autoRepeat
- autoCollect

---

### ModifierOperation

- set
- add
- multiply
- min
- max

---

### ScopeKind

- global
- zone
- node
- nodeTag
- resource

---

### PriceCurveType

- exponential
- linear
- table
- segments

---

### RequirementType

- unlock
- resourceAtLeast
- nodeLevelAtLeast
- hasUpgrade

---

### PrestigeFormulaType

- sqrt
- linear
- log

---

## Guidelines

- Values are case-sensitive.
- Do not rename existing values after release.
- Add new values rather than modifying existing ones.
- Remove unused values only after confirming no references exist.
