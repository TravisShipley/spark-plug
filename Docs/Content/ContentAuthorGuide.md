---
document_role: process
topic: content
audience: designers, developers
scope: content
status: active
---

# Spark Plug – Content Author Guide

This guide explains how to safely edit the Spark Plug Google Sheets.

---

## General Rules

1. Every row represents a record.
2. IDs must be unique.
3. Do not rename IDs after release.
4. Empty rows are ignored.
5. Rows starting with `//` are treated as comments.
6. Tabs starting with `//` are ignored by the importer.
7. Tabs starting with `__` are for internal system metadata.

---

## IDs

- Use dot notation for readability:
  - `nodeType.business.lemonade`
  - `upgrade.manager.lemonade`
- IDs are permanent. Treat them like database keys.

---

## Bracket Syntax (Resource Paths)

Some fields reference a **resource-specific property** using bracket syntax.

Format:

```
baseName[resourceId]
```

Examples:

- `nodeOutput[currencySoft]`
- `resourceGain[currencySoft]`
- `resource[currencyMeta]`
- `lifetimeEarnings[currencySoft]`

### When to Use Brackets

Use bracket syntax when:

- A field represents a **path or target**
- The value depends on a specific `resourceId`
- Examples include:
  - `Modifiers.target`
  - Formula fields such as `basedOn`
  - Any column that references a resource-specific stat

### When NOT to Use Brackets

Do **not** use bracket syntax for normal ID fields:

- `nodeId`
- `resource`
- `zoneId`
- `upgradeId`
- Any column that directly stores an ID value

Those should remain plain IDs (no brackets).

### Rule of Thumb

If the column describes **what property to modify or read**, use:

```
propertyName[resourceId]
```

If the column describes **which object**, use the ID directly.

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
`[{“resource”:currencySoft,“amount”:“100”}]`

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
