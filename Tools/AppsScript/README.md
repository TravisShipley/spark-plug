---
document_role: reference
topic: tools
audience: developers, designers
scope: system:data
status: draft
---

# Spark Plug Sheets Tools (Apps Script)

This folder contains Google Apps Script tooling that keeps the Spark Plug datasheet aligned with the authoritative `sheetSpec` in the schema JSON.

These tools are designed to make Sheets **self-healing** as the engine/schema evolves.

Files:

- `code.gs` → minimal `onOpen()` bootstrap
- `spark_plug_sheet_tools.gs` → structure, dropdown, and validation logic

---

## What this tool does

Adds a **SparkPlug** menu to your Google Sheet with:

1. **Ensure Tabs & Headers (from sheetSpec)**
   - Creates missing tabs (sheets) defined by `sheetSpec.tables`
   - Ensures each tab has the required header columns
   - Appends missing headers to the end of the header row
   - Optionally freezes the header row

2. **Refresh Dropdowns (from sheetSpec)**
   - Creates/rewrites a `Dropdowns` tab (tooling-owned)
   - Writes enum option lists from `sheetSpec.enums`
   - Applies data validation rules for columns that specify `enumRef`

3. **Validate Sheet Structure (from sheetSpec)**
   - Produces a report of missing tabs/columns and duplicates
   - Does **not** modify the spreadsheet

---

## What this tool will NOT do (safety rules)

The tools are intentionally conservative:

- ✅ Will **not** delete any tabs
- ✅ Will **not** delete any columns
- ✅ Will **not** reorder existing columns
- ✅ Will **not** overwrite any non-header data rows
- ✅ Will **not** clear invalid values already entered (validation prevents future invalid values)

The only sheet that may be fully rewritten is:

- `Dropdowns` (tooling-owned)

---

## Preconditions

Your Google Sheet should contain a tab named:

- `PackMeta`

With headers in row 1:

- `key`
- `value`

`PackMeta` must include (or the script will prompt you to add):

- `schemaUrl` → URL to the schema JSON containing `sheetSpec`

Optional keys:

- `schemaVersion` → informational (the script may warn if mismatched)

Example `PackMeta` rows:

| key           | value                           |
| ------------- | ------------------------------- |
| schemaUrl     | (raw github url to schema JSON) |
| schemaVersion | 0.2                             |

---

## Installation

1. Open your Google Sheet
2. Go to **Extensions → Apps Script**
3. Add these script files:
   - `code.gs`
   - `spark_plug_sheet_tools.gs`
4. Save

On the next reload of the spreadsheet, you should see a **SparkPlug** menu.

If your Apps Script project already has its own `onOpen()`, keep that as the single entry point and call `addSparkPlugSheetToolsMenuItems_(menu)` from it before `addToUi()`.

---

## First Run (Permissions)

On first run, Apps Script will request permissions for:

- reading and editing the spreadsheet
- fetching the schema JSON from `schemaUrl` via `UrlFetchApp`

This is required for the tools to function.

---

## Usage

### Ensure Tabs & Headers

Use when:

- the schema changed
- a new table/column was added
- you pulled a new version of Spark Plug tooling

What it does:

- Creates missing tabs and adds missing headers (append-only)

### Refresh Dropdowns

Use when:

- enum lists changed
- you added new enumRef columns
- you want to reapply validation rules

What it does:

- Rewrites `Dropdowns`
- Applies validation rules to enumRef columns

### Validate Sheet Structure

Use when:

- before exporting/importing a pack
- you suspect drift
- you want a report without changing anything

---

## Common Troubleshooting

### “schemaUrl missing”

- Add a `schemaUrl` row in PackMeta or run the tool and enter it when prompted.

### “Fetch failed” or non-200 response

- Verify `schemaUrl` is reachable from a browser.
- If using GitHub raw URLs, ensure the URL points to the raw file.
- Confirm the file contains a top-level `sheetSpec` object.

### Tabs created but headers not applied

- Check `sheetSpec.settings.headerRowIndex` (default is 1).
- Ensure your sheet doesn't have merged header cells that prevent writing.

### Dropdown validation not appearing

- Confirm `sheetSpec.columns[].enumRef` is present for the column.
- Run **Refresh Dropdowns** again.
- Ensure the target tab has the column header spelled exactly as in sheetSpec.

---

## Notes on Drift and Source of Truth

- `sheetSpec` defines **sheet structure** (tabs/headers/enums).
- The spreadsheet defines **content values** (rows).
- Runtime does not require the sheet to be “complete”, but engine authoring benefits from keeping structure aligned.

---

## Roadmap (optional)

Possible future improvements:

- “Strict mode” to warn/error on unknown extra columns
- Per-table required vs optional enforcement
- Column grouping / insertion policies (instead of append-only)
- A CI step that runs `Validate Sheet Structure` and exports a summary report
