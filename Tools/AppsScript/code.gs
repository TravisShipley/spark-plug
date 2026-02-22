/**
 * Spark Plug - CSV Pack -> Tabs Importer
 *
 * Usage:
 * 1) Put all CSV files (one per tab) in a Drive folder.
 * 2) Set CSV_FOLDER_ID below to that folder's ID.
 * 3) Reload spreadsheet -> SparkPlug menu -> Import CSV Pack
 */

const CSV_FOLDER_ID = "";

// Optional: enforce a stable import order (recommended).
// Any CSV not in this list will be imported afterward, alphabetically.
const PREFERRED_TAB_ORDER = [
  "PackMeta",
  "Resources",
  "Phases",
  "Zones",
  "ComputedVars",
  "Nodes",
  "NodeInputs",
  "NodeOutputs",
  "NodeCapacities",
  "NodeOutputScaling",
  "NodeCapacityScaling",
  "NodePriceCurveTable",
  "NodePriceCurveSegments",
  "NodeRequirements",
  "NodeInstances",
  "Links",
  "Modifiers",
  "Upgrades",
  "Milestones",
  "Projects",
  "UnlockGraph",
  "Buffs",
  "Prestige",
];

// Max cells per write batch (Sheets limit-ish). Keep conservative.
const WRITE_BATCH_ROWS = 1000;

function onOpen() {
  SpreadsheetApp.getUi()
    .createMenu("SparkPlug")
    .addItem("Import CSV Schema Pack", "importCsvSchemaIntoTabs")
    .addItem("Import CSV Data Pack", "importCsvDataPackIntoTabs")
    .addItem("Format Sheets", "formatSheets")
    .addItem("Set CSV Folder…", "promptForCsvFolder")
    .addSeparator()
    .addItem("Dry Run (log only)", "importCsvPackDryRun")
    .addSeparator()
    .addItem("Create // Sheet Definition", "createSheetDefinitionPage")
    .addToUi();
}

/**
 * Imports all *.csv files from the configured folder into tabs.
 * - Tab name = file name without .csv
 * - Existing tabs are cleared & overwritten (but preserved if not in CSV pack)
 */
function importCsvSchemaIntoTabs() {
  const folderId = getCsvFolderId_();
  if (!folderId) {
    throw new Error("CSV folder not set. Use SparkPlug → Set CSV Folder…");
  }

  const ss = SpreadsheetApp.getActive();
  const files = listCsvFiles_(folderId);
  const plan = buildImportPlan_(files);

  Logger.log(
    `Found ${files.length} CSV files. Import order: ${plan.map((p) => p.tabName).join(", ")}`,
  );

  for (const item of plan) {
    importOneCsvToTab_(ss, item.file, item.tabName);
  }

  SpreadsheetApp.getUi().alert(`Imported ${plan.length} CSV files into tabs.`);
}

/**
 * Imports all *.csv files from the configured folder into existing tabs,
 * overwriting DATA ONLY (rows 2+), preserving existing header row (row 1).
 *
 * Validation:
 * - Errors if a matching sheet does not exist.
 * - Errors if CSV headers do not exactly match the existing sheet headers.
 * - Errors if any row has a different column count than the headers.
 * - For columns ending with "_json", errors if non-empty cells are not valid JSON.
 */
function importCsvDataPackIntoTabs() {
  const folderId = getCsvFolderId_();
  if (!folderId) {
    throw new Error("CSV folder not set. Use SparkPlug → Set CSV Folder…");
  }

  const ss = SpreadsheetApp.getActive();
  const files = listCsvFiles_(folderId);
  const plan = buildImportPlan_(files);

  Logger.log(
    `Found ${files.length} CSV files. Data import order: ${plan.map((p) => p.tabName).join(", ")}`,
  );

  for (const item of plan) {
    importOneCsvDataToExistingTab_(ss, item.file, item.tabName);
  }

  SpreadsheetApp.getUi().alert(
    `Imported data for ${plan.length} CSV files into existing tabs.`,
  );
}

/**
 * Same as import, but doesn't modify sheets. Useful for confirming ordering and names.
 */
function importCsvPackDryRun() {
  const folderId = getCsvFolderId_();
  if (!folderId) {
    throw new Error("CSV folder not set. Use SparkPlug → Set CSV Folder…");
  }
  const files = listCsvFiles_(folderId);
  const plan = buildImportPlan_(files);
  Logger.log("Dry run import plan:");
  for (const item of plan) {
    Logger.log(`- ${item.tabName} <= ${item.file.getName()}`);
  }
  SpreadsheetApp.getUi().alert(`Dry run complete. Check Apps Script Logs.`);
}

/**
 * Formats every sheet in the spreadsheet:
 * - default font size: 12
 * - row 1: dark background, white font
 * - columns: minimum column width 100, and padded to (auto-resize + 50)
 */
function formatSheets() {
  const ss = SpreadsheetApp.getActive();
  const sheets = ss.getSheets();

  for (const sheet of sheets) {
    const lastRow = Math.max(sheet.getLastRow(), 1);
    const lastCol = Math.max(sheet.getLastColumn(), 1);

    // Set default font size (apply to used range so it’s fast)
    sheet.getRange(1, 1, lastRow, lastCol).setFontSize(12);

    // Header row styling (do not change this color)
    sheet
      .getRange(1, 1, 1, lastCol)
      .setBackground("#252525")
      .setFontColor("#ffffff");

    // Column widths: auto-resize then add padding, respecting minimum width
    sheet.autoResizeColumns(1, lastCol);

    for (let c = 1; c <= lastCol; c++) {
      const autoWidth = sheet.getColumnWidth(c);
      const paddedWidth = autoWidth + 50;
      const finalWidth = Math.max(100, paddedWidth);
      sheet.setColumnWidth(c, finalWidth);
    }
  }

  SpreadsheetApp.getUi().alert("Formatted all sheets.");
}

function getCsvFolderId_() {
  return (
    PropertiesService.getDocumentProperties().getProperty("CSV_FOLDER_ID") ||
    CSV_FOLDER_ID ||
    ""
  );
}

/**
 * Prompts the user to paste a Google Drive folder URL or folder ID containing the CSV pack,
 * validates it, and stores it in document properties for future imports.
 */
function promptForCsvFolder() {
  const ui = SpreadsheetApp.getUi();

  const result = ui.prompt(
    "Set CSV Folder",
    "Paste the Google Drive folder URL or folder ID containing the CSV pack:",
    ui.ButtonSet.OK_CANCEL,
  );

  if (result.getSelectedButton() !== ui.Button.OK) {
    return;
  }

  const input = result.getResponseText().trim();
  const folderId = extractFolderId_(input);

  if (!folderId) {
    ui.alert(
      "Invalid folder",
      "Could not extract a folder ID. Please paste a valid Google Drive folder URL or ID.",
      ui.ButtonSet.OK,
    );
    return;
  }

  // Validate access
  try {
    DriveApp.getFolderById(folderId).getName();
  } catch (e) {
    ui.alert(
      "Access error",
      "Folder not found or you do not have access.",
      ui.ButtonSet.OK,
    );
    return;
  }

  PropertiesService.getDocumentProperties().setProperty(
    "CSV_FOLDER_ID",
    folderId,
  );
  ui.alert("CSV folder set successfully.");
}

function extractFolderId_(input) {
  if (!input) return null;

  // If it already looks like an ID
  if (/^[a-zA-Z0-9_-]{10,}$/.test(input)) {
    return input;
  }

  // Extract from URL like https://drive.google.com/drive/folders/<id>
  const match = input.match(/\/folders\/([a-zA-Z0-9_-]+)/);
  if (match && match[1]) {
    return match[1];
  }

  return null;
}

/**
 * Creates a new sheet (name prefixed with "//") that contains:
 * 1) A GRID summary: Sheet | Header Count | Header CSV (copy/paste friendly)
 * 2) A BLOB summary: blocks like:
 *      [SheetName]
 *      colA,colB,colC
 *
 * Skips any existing sheets that start with "//".
 */
function createSheetDefinitionPage() {
  const ss = SpreadsheetApp.getActive();
  const ui = SpreadsheetApp.getUi();

  const baseName = "//Sheet Definition";
  const defName = getUniqueSheetName_(ss, baseName);
  const defSheet = ss.insertSheet(defName);

  const sourceSheets = ss
    .getSheets()
    .filter((sh) => sh.getSheetId() !== defSheet.getSheetId())
    .filter((sh) => !String(sh.getName() || "").startsWith("//"));

  // ---------
  // Build GRID
  // ---------
  const gridValues = [["Sheet", "Header Count", "Headers (CSV)"]];

  // We'll also collect a max header length for optional UI sizing decisions
  for (const sh of sourceSheets) {
    const name = sh.getName();
    const headers = getTrimmedHeaderRow_(sh); // array of strings (possibly empty)
    const headerCsv =
      headers.length > 0 ? headers.map(csvEscape_).join(",") : "";
    gridValues.push([name, headers.length, headerCsv]);
  }

  // Write grid at A1
  const gridRows = gridValues.length;
  const gridCols = gridValues[0].length;
  defSheet.getRange(1, 1, gridRows, gridCols).setValues(gridValues);

  // Light grid formatting (do not touch your global header style rules elsewhere)
  const gridHeaderRange = defSheet.getRange(1, 1, 1, gridCols);
  gridHeaderRange.setFontWeight("bold");
  defSheet.setFrozenRows(1);

  defSheet.autoResizeColumns(1, gridCols);
  // Make headers column comfortably wide
  defSheet.setColumnWidth(1, Math.max(180, defSheet.getColumnWidth(1)));
  defSheet.setColumnWidth(2, Math.max(120, defSheet.getColumnWidth(2)));
  defSheet.setColumnWidth(3, Math.max(700, defSheet.getColumnWidth(3)));

  // Wrap the header CSV column for readability
  defSheet.getRange(2, 3, Math.max(gridRows - 1, 1), 1).setWrap(true);

  // ---------
  // Build BLOB
  // ---------
  const blocks = [];
  for (const sh of sourceSheets) {
    const name = sh.getName();
    const headers = getTrimmedHeaderRow_(sh);
    const headerCsv =
      headers.length > 0 ? headers.map(csvEscape_).join(",") : "";
    // Always include the sheet name, even if empty/no headers
    blocks.push(`[${name}]\n${headerCsv}\n`);
  }
  const blobText = blocks.join("\n");

  // Place blob below grid with a little spacing
  const blobTitleRow = gridRows + 2;
  const blobRow = blobTitleRow + 1;

  defSheet
    .getRange(blobTitleRow, 1)
    .setValue("Copy/Paste Blob (paste this into chat):");
  defSheet.getRange(blobTitleRow, 1).setFontWeight("bold");

  // Put the blob in a single cell for easy copy/paste
  defSheet.getRange(blobRow, 1).setValue(blobText);

  // Make blob area readable
  defSheet
    .getRange(blobRow, 1)
    .setWrap(true)
    .setFontFamily("Menlo")
    .setFontSize(11);

  // Give the blob cell room (best-effort; row height caps vary)
  try {
    defSheet.setRowHeight(blobRow, 600);
  } catch (e) {
    // Ignore if Sheets refuses (rare)
  }

  // Make column A wide enough for the blob too (keeps it copy-friendly)
  defSheet.setColumnWidth(1, Math.max(defSheet.getColumnWidth(1), 900));

  // Cursor on the blob cell by default (most common action)
  defSheet.setActiveRange(defSheet.getRange(blobRow, 1));

  ui.alert(`Created "${defName}". Grid at top; blob starts at A${blobRow}.`);
}

/* -----------------------------
 * Internals
 * ----------------------------- */

function listCsvFiles_(folderId) {
  const folder = DriveApp.getFolderById(folderId);
  const it = folder.getFiles();
  const csvFiles = [];

  while (it.hasNext()) {
    const f = it.next();
    const name = f.getName();
    if (name.toLowerCase().endsWith(".csv")) {
      csvFiles.push(f);
    }
  }

  // Sort alphabetically by filename for stable behavior
  csvFiles.sort((a, b) => a.getName().localeCompare(b.getName()));
  return csvFiles;
}

function buildImportPlan_(csvFiles) {
  // Map tabName -> file (last one wins if duplicates)
  const map = {};
  for (const f of csvFiles) {
    const tabName = sanitizeTabName_(stripCsvExt_(f.getName()));
    map[tabName] = f;
  }

  const plan = [];

  // Preferred order first (stable)
  for (const tabName of PREFERRED_TAB_ORDER) {
    const safeName = sanitizeTabName_(tabName);
    if (map[safeName]) {
      plan.push({ tabName: safeName, file: map[safeName] });
      delete map[safeName];
    }
  }

  // Any remaining files are imported after, in stable alphabetical order
  const remainingTabNames = Object.keys(map).sort((a, b) => a.localeCompare(b));
  if (remainingTabNames.length > 0) {
    Logger.log(
      `Warning: ${remainingTabNames.length} CSV(s) not in PREFERRED_TAB_ORDER will be imported after known tabs: ${remainingTabNames.join(", ")}`,
    );
  }

  for (const tabName of remainingTabNames) {
    plan.push({ tabName, file: map[tabName] });
  }

  return plan;
}

function importOneCsvToTab_(ss, file, tabName) {
  const blob = file.getBlob();
  const csvText = blob.getDataAsString("UTF-8");

  // Parse CSV into 2D array
  const values = Utilities.parseCsv(csvText);

  // Ensure at least 1 row/col
  if (!values || values.length === 0) {
    Logger.log(`Skipping empty CSV: ${file.getName()}`);
    return;
  }

  const sheet = getOrCreateSheet_(ss, tabName);

  // Clear existing content
  sheet.clearContents();
  sheet.clearFormats();

  // Write values in batches (handles big sheets)
  writeValuesBatched_(sheet, values);

  // Light formatting
  sheet.setFrozenRows(1);
  sheet.autoResizeColumns(1, Math.min(values[0].length, 30)); // cap auto-resize to avoid slowness

  Logger.log(
    `Imported ${file.getName()} -> tab "${tabName}" (${values.length} rows)`,
  );
}

function importOneCsvDataToExistingTab_(ss, file, tabName) {
  const sheet = ss.getSheetByName(tabName);
  if (!sheet) {
    throw new Error(
      `Missing sheet "${tabName}". Data pack import requires pre-existing tabs with headers.`,
    );
  }

  const blob = file.getBlob();
  const csvText = blob.getDataAsString("UTF-8");
  const values = Utilities.parseCsv(csvText);

  if (!values || values.length === 0) {
    Logger.log(`Skipping empty CSV: ${file.getName()}`);
    return;
  }

  const csvHeaders = normalizeRowToStrings_(values[0]);
  const lastCol = Math.max(sheet.getLastColumn(), 1);

  // Read existing headers from sheet row 1 across lastCol, then trim trailing empties
  let sheetHeaders = sheet
    .getRange(1, 1, 1, lastCol)
    .getValues()[0]
    .map((v) => String(v ?? "").trim());
  sheetHeaders = trimTrailingEmpty_(sheetHeaders);

  // Trim trailing empty headers in CSV too (in case of extra commas)
  const csvHeadersTrimmed = trimTrailingEmpty_(csvHeaders);

  if (!arraysEqual_(sheetHeaders, csvHeadersTrimmed)) {
    throw new Error(
      `Header mismatch on "${tabName}".\n` +
        `Sheet headers: ${JSON.stringify(sheetHeaders)}\n` +
        `CSV headers:   ${JSON.stringify(csvHeadersTrimmed)}`,
    );
  }

  const headerLen = sheetHeaders.length;
  const jsonColIndexes = findJsonColumnIndexes_(sheetHeaders);

  // Validate row widths and JSON cells
  for (let r = 1; r < values.length; r++) {
    const row = normalizeRowToStrings_(values[r]);
    const trimmedRow = normalizeRowLength_(row, headerLen);

    if (trimmedRow.length !== headerLen) {
      throw new Error(
        `Row ${r + 1} in "${tabName}" has ${trimmedRow.length} cols; expected ${headerLen}.`,
      );
    }

    // Validate *_json columns
    for (const c of jsonColIndexes) {
      const cell = trimmedRow[c];
      if (cell && cell.trim() !== "") {
        try {
          JSON.parse(cell);
        } catch (e) {
          throw new Error(
            `Invalid JSON in "${tabName}" at row ${r + 1}, col ${c + 1} ("${sheetHeaders[c]}"): ${cell}`,
          );
        }
      }
    }
  }

  // Overwrite data only (rows 2+), preserve header row and formatting
  const bodyRows = values
    .slice(1)
    .map((r) => normalizeRowLength_(normalizeRowToStrings_(r), headerLen));

  // Clear existing body (contents only)
  const lastRow = Math.max(sheet.getLastRow(), 1);
  if (lastRow > 1 && headerLen > 0) {
    sheet.getRange(2, 1, lastRow - 1, headerLen).clearContent();
  }

  // Write new body if present
  if (bodyRows.length > 0 && headerLen > 0) {
    sheet.getRange(2, 1, bodyRows.length, headerLen).setValues(bodyRows);
  }

  Logger.log(
    `Imported DATA ${file.getName()} -> tab "${tabName}" (${bodyRows.length} data rows)`,
  );
}

function writeValuesBatched_(sheet, values) {
  const numRows = values.length;
  const numCols = Math.max(...values.map((r) => r.length));

  // Normalize row lengths (Sheets requires rectangular)
  const rect = values.map((r) => {
    const row = r.slice();
    while (row.length < numCols) row.push("");
    return row;
  });

  // Write in row batches
  for (let start = 0; start < numRows; start += WRITE_BATCH_ROWS) {
    const end = Math.min(start + WRITE_BATCH_ROWS, numRows);
    const chunk = rect.slice(start, end);

    sheet.getRange(start + 1, 1, chunk.length, numCols).setValues(chunk);
  }
}

function getOrCreateSheet_(ss, name) {
  const existing = ss.getSheetByName(name);
  if (existing) return existing;

  const sheet = ss.insertSheet(name);
  return sheet;
}

function stripCsvExt_(filename) {
  return filename.replace(/\.csv$/i, "");
}

function sanitizeTabName_(name) {
  // Google Sheets tab name rules:
  // - max 100 chars
  // - cannot contain: : \ / ? * [ ]
  let n = String(name || "").trim();
  n = n.replace(/[:\\\/\?\*\[\]]/g, " ");
  n = n.substring(0, 100);
  if (!n) n = "Sheet";
  return n;
}

function normalizeRowToStrings_(row) {
  return (row || []).map((v) => String(v ?? "").trim());
}

function trimTrailingEmpty_(arr) {
  let end = arr.length;
  while (end > 0 && String(arr[end - 1] ?? "").trim() === "") end--;
  return arr.slice(0, end);
}

function arraysEqual_(a, b) {
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) {
    if (a[i] !== b[i]) return false;
  }
  return true;
}

function normalizeRowLength_(row, len) {
  const out = row.slice(0, len);
  while (out.length < len) out.push("");
  return out;
}

function findJsonColumnIndexes_(headers) {
  const indexes = [];
  for (let i = 0; i < headers.length; i++) {
    if (String(headers[i]).toLowerCase().endsWith("_json")) indexes.push(i);
  }
  return indexes;
}

/**
 * Returns the trimmed header row (row 1) for a sheet as array of strings,
 * trimming trailing empty headers.
 */
function getTrimmedHeaderRow_(sheet) {
  const lastCol = Math.max(sheet.getLastColumn(), 1);
  let headers = sheet
    .getRange(1, 1, 1, lastCol)
    .getValues()[0]
    .map((v) => String(v ?? "").trim());
  headers = trimTrailingEmpty_(headers);
  return headers;
}

/**
 * Returns a unique sheet name by appending " (2)", " (3)", etc. if needed.
 */
function getUniqueSheetName_(ss, baseName) {
  let name = baseName;
  let i = 2;
  while (ss.getSheetByName(name)) {
    name = `${baseName} (${i})`;
    i++;
  }
  return name;
}

/**
 * Escapes a value for a single CSV field.
 * Quotes if needed and doubles internal quotes.
 */
function csvEscape_(value) {
  const s = String(value ?? "");
  if (/[",\n\r]/.test(s)) {
    return `"${s.replace(/"/g, '""')}"`;
  }
  return s;
}
