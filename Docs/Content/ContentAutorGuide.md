# Spark Plug – Content Author Guide

This guide explains how to safely edit the Spark Plug Google Sheets.

---

## General Rules

1. Every row represents a record.
2. IDs must be unique.
3. Do not rename IDs after release.
4. Empty rows are ignored.
5. Rows starting with `_` are treated as comments.
6. Tabs starting with `_` are ignored by the importer.

---

## IDs

- Use dot notation for readability:
  - `nodeType.business.lemonade`
  - `upgrade.manager.lemonade`
- IDs are permanent. Treat them like database keys.

---

## Dropdown Fields

Fields backed by `__Enums` must use dropdown values.

If a value is missing:

1. Add it to `__Enums`
2. Refresh validation

---

## JSON Fields

Columns ending in `_json` contain structured data.

Example:  
`[{“resource”:“cash”,“amount”:“100”}]`

---

Guidelines:

- Must be valid JSON
- Use double quotes
- Do not include trailing commas

---

## Relationships

Many tables reference IDs from other tables.

Examples:

- `NodeOutputs.nodeId` → `Nodes.id`
- `Upgrades.effects_json` → `Modifiers.id`

If a reference is wrong, import will fail.

---

## Safe Editing Workflow

1. Duplicate the sheet before large changes.
2. Make edits.
3. Run importer.
4. Fix any validation errors.
5. Commit both sheet and generated data.

---

## Common Mistakes

| Issue               | Cause            |
| ------------------- | ---------------- |
| Import fails        | Invalid JSON     |
| Data missing        | ID typo          |
| Dropdown empty      | Enum not defined |
| Unexpected behavior | Duplicate IDs    |

---

## Best Practices

- Prefer adding rows over modifying existing ones.
- Keep enum values stable.
- Group related records together.
- Use tags instead of creating many special-case systems.
