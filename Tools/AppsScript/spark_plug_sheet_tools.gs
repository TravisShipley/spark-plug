/**
 * Spark Plug sheet tooling driven by schema.sheetSpec.
 *
 * This file does not declare its own onOpen() because this Apps Script project
 * already has one in code.gs. That onOpen() should call
 * addSparkPlugSheetToolsMenuItems_(menu) before addToUi().
 */

var SPARKPLUG_DROPDOWNS_SHEET_NAME_ = "Dropdowns";
var SPARKPLUG_VALIDATION_MAX_ROW_ = 1000;
var SPARKPLUG_HEADER_FONT_COLOR_ = "#ffffff";
var SPARKPLUG_HEADER_BACKGROUND_COLOR_ = "#000000";

function addSparkPlugSheetToolsMenuItems_(menu) {
  menu
    .addSeparator()
    .addItem(
      "Ensure Tabs & Headers (from sheetSpec)",
      "ensureTabsAndHeadersFromSheetSpec",
    )
    .addItem(
      "Refresh Dropdowns (from sheetSpec)",
      "refreshDropdownsFromSheetSpec",
    )
    .addItem(
      "Validate Sheet Structure (from sheetSpec)",
      "validateSheetStructureFromSheetSpec",
    );

  return menu;
}

function ensureTabsAndHeadersFromSheetSpec() {
  var ui = SpreadsheetApp.getUi();

  try {
    var context = loadSheetSpecContext_(true);
    var sheetSpec = context.sheetSpec;
    var settings = context.settings;
    var report = {
      createdTabs: [],
      addedColumnsByTab: {},
      frozenTabs: [],
      skippedOptionalTabs: [],
    };

    var tables = Array.isArray(sheetSpec.tables) ? sheetSpec.tables : [];
    for (var i = 0; i < tables.length; i++) {
      var tableSpec = tables[i];
      var tableName = String((tableSpec && tableSpec.name) || "").trim();
      if (!tableName) {
        continue;
      }

      var existingSheet =
        SpreadsheetApp.getActiveSpreadsheet().getSheetByName(tableName);
      if (tableSpec.required !== true && !existingSheet) {
        report.skippedOptionalTabs.push(tableName);
        continue;
      }

      var result = ensureTableFromSheetSpec_(tableSpec, settings);
      if (result.createdSheet) {
        report.createdTabs.push(tableName);
      }
      if (result.addedColumns.length > 0) {
        report.addedColumnsByTab[tableName] = result.addedColumns;
      }
      if (result.frozeHeaderRow) {
        report.frozenTabs.push(tableName);
      }
    }

    var summary = buildEnsureTabsSummary_(report);
    Logger.log(summary);
    ui.alert("SparkPlug", summary, ui.ButtonSet.OK);
  } catch (err) {
    handleSparkPlugError_("Ensure Tabs & Headers failed", err);
  }
}

function refreshDropdownsFromSheetSpec() {
  var ui = SpreadsheetApp.getUi();

  try {
    var context = loadSheetSpecContext_(true);
    var sheetSpec = context.sheetSpec;
    var settings = context.settings;
    var enumTable = buildEnumTable_(sheetSpec);
    var dropdownSheet = getOrCreateSheet_(SPARKPLUG_DROPDOWNS_SHEET_NAME_);
    var rewriteInfo = rewriteDropdownSheet_(dropdownSheet, enumTable);
    var validationReport = {
      validationsApplied: 0,
      examples: [],
      warnings: [],
    };
    var enumColumnIndexByKey = {};
    var i;

    for (i = 0; i < enumTable.enumKeys.length; i++) {
      enumColumnIndexByKey[enumTable.enumKeys[i]] = i + 1;
    }

    var tables = Array.isArray(sheetSpec.tables) ? sheetSpec.tables : [];
    for (i = 0; i < tables.length; i++) {
      var tableSpec = tables[i];
      var tableName = String((tableSpec && tableSpec.name) || "").trim();
      if (!tableName) {
        continue;
      }

      var sheet =
        SpreadsheetApp.getActiveSpreadsheet().getSheetByName(tableName);
      if (!sheet) {
        validationReport.warnings.push(
          'Skipped validations for missing sheet "' + tableName + '".',
        );
        continue;
      }

      var header = readHeader_(sheet, settings.headerRowIndex);
      var headerInfo = analyzeHeader_(header);
      var columns = Array.isArray(tableSpec.columns) ? tableSpec.columns : [];

      for (var c = 0; c < columns.length; c++) {
        var columnSpec = columns[c] || {};
        var enumRef = String(columnSpec.enumRef || "").trim();
        if (!enumRef) {
          continue;
        }

        var declaredHeader = String(columnSpec.name || "").trim();
        if (!declaredHeader) {
          continue;
        }

        var normalizedHeader = normalizeHeaderName_(declaredHeader);
        var targetColumnIndex = headerInfo.indexByNormalized[normalizedHeader];
        if (!targetColumnIndex) {
          validationReport.warnings.push(
            'Skipped validation for "' +
              tableName +
              "." +
              declaredHeader +
              '" because the header is missing.',
          );
          continue;
        }

        var resolvedEnumKey = resolveEnumKey_(enumRef, enumColumnIndexByKey);
        if (!resolvedEnumKey) {
          validationReport.warnings.push(
            'Skipped validation for "' +
              tableName +
              "." +
              declaredHeader +
              '" because enum "' +
              enumRef +
              '" was not found.',
          );
          continue;
        }

        var dropdownRange = getDropdownRangeForEnum_(
          dropdownSheet,
          enumColumnIndexByKey[resolvedEnumKey],
        );
        if (!dropdownRange) {
          validationReport.warnings.push(
            'Skipped validation for "' +
              tableName +
              "." +
              declaredHeader +
              '" because enum "' +
              resolvedEnumKey +
              '" has no values.',
          );
          continue;
        }

        var startRow = settings.headerRowIndex + 1;
        var endRow = Math.min(
          sheet.getMaxRows(),
          SPARKPLUG_VALIDATION_MAX_ROW_,
        );
        if (endRow < startRow) {
          validationReport.warnings.push(
            'Skipped validation for "' +
              tableName +
              "." +
              declaredHeader +
              '" because there are no editable rows below the header.',
          );
          continue;
        }

        var targetRange = sheet.getRange(
          startRow,
          targetColumnIndex,
          endRow - startRow + 1,
          1,
        );
        var rule = SpreadsheetApp.newDataValidation()
          .requireValueInRange(dropdownRange, true)
          .setAllowInvalid(true)
          .build();

        targetRange.setDataValidation(rule);
        validationReport.validationsApplied += 1;

        if (validationReport.examples.length < 10) {
          validationReport.examples.push(
            tableName + "." + declaredHeader + " -> " + resolvedEnumKey,
          );
        }
      }
    }

    var summary = buildRefreshDropdownsSummary_(rewriteInfo, validationReport);
    Logger.log(summary);
    ui.alert("SparkPlug", summary, ui.ButtonSet.OK);
  } catch (err) {
    handleSparkPlugError_("Refresh Dropdowns failed", err);
  }
}

function validateSheetStructureFromSheetSpec() {
  var ui = SpreadsheetApp.getUi();

  try {
    var context = loadSheetSpecContext_(true);
    var sheetSpec = context.sheetSpec;
    var settings = context.settings;
    var report = {
      errors: [],
      warnings: [],
      okTables: [],
    };

    var tables = Array.isArray(sheetSpec.tables) ? sheetSpec.tables : [];
    for (var i = 0; i < tables.length; i++) {
      var tableSpec = tables[i] || {};
      var tableName = String(tableSpec.name || "").trim();
      if (!tableName) {
        continue;
      }

      var sheet =
        SpreadsheetApp.getActiveSpreadsheet().getSheetByName(tableName);
      if (!sheet) {
        if (tableSpec.required === true) {
          report.errors.push('Missing required sheet "' + tableName + '".');
        }
        continue;
      }

      var header = readHeader_(sheet, settings.headerRowIndex);
      var headerInfo = analyzeHeader_(header);
      var tableHasErrors = false;
      var tableHasWarnings = false;

      if (headerInfo.lastUsedColumn === 0) {
        report.warnings.push(
          'Sheet "' +
            tableName +
            '" has an empty header row at row ' +
            settings.headerRowIndex +
            ".",
        );
        tableHasWarnings = true;
      }

      if (headerInfo.blankColumnIndexes.length > 0) {
        report.warnings.push(
          'Sheet "' +
            tableName +
            '" has blank header cells at columns ' +
            headerInfo.blankColumnIndexes.join(", ") +
            ".",
        );
        tableHasWarnings = true;
      }

      if (headerInfo.duplicates.length > 0) {
        report.errors.push(
          'Sheet "' +
            tableName +
            '" has duplicate headers (case-insensitive): ' +
            headerInfo.duplicates.join(", ") +
            ".",
        );
        tableHasErrors = true;
      }

      var declaredColumns = getDeclaredColumns_(tableSpec);
      for (var c = 0; c < declaredColumns.length; c++) {
        var declared = declaredColumns[c];
        if (
          declared.required &&
          !headerInfo.indexByNormalized[normalizeHeaderName_(declared.name)]
        ) {
          report.errors.push(
            'Sheet "' +
              tableName +
              '" is missing required column "' +
              declared.name +
              '".',
          );
          tableHasErrors = true;
        }
      }

      if (!settings.allowExtraColumns) {
        var extraHeaders = [];
        for (var h = 0; h < headerInfo.headersWithinUsedRange.length; h++) {
          var headerName = headerInfo.headersWithinUsedRange[h];
          if (!headerName) {
            continue;
          }

          if (
            !declaredColumns.normalizedNameSet[normalizeHeaderName_(headerName)]
          ) {
            extraHeaders.push(headerName);
          }
        }

        if (extraHeaders.length > 0) {
          report.warnings.push(
            'Sheet "' +
              tableName +
              '" has extra columns not declared in sheetSpec: ' +
              extraHeaders.join(", ") +
              ".",
          );
          tableHasWarnings = true;
        }
      }

      if (!tableHasErrors && !tableHasWarnings) {
        report.okTables.push(tableName);
      }
    }

    var summary = buildValidateStructureSummary_(report, tables.length);
    Logger.log(summary);
    ui.alert("SparkPlug", summary, ui.ButtonSet.OK);
  } catch (err) {
    handleSparkPlugError_("Validate Sheet Structure failed", err);
  }
}

function getManifestValue_(key) {
  var layout = getManifestLayout_();
  var lookupKey = String(key || "").trim();
  if (!lookupKey) {
    return null;
  }

  var sheet = layout.sheet;
  var lastRow = sheet.getLastRow();
  if (lastRow < 2) {
    return null;
  }

  var width = Math.max(layout.keyColumn, layout.valueColumn);
  var values = sheet.getRange(2, 1, lastRow - 1, width).getValues();
  for (var i = 0; i < values.length; i++) {
    var rowKey = String(values[i][layout.keyColumn - 1] || "").trim();
    if (rowKey === lookupKey) {
      var value = String(values[i][layout.valueColumn - 1] || "").trim();
      return value || null;
    }
  }

  return null;
}

function setManifestValue_(key, value) {
  var layout = getManifestLayout_();
  var writeKey = String(key || "").trim();
  var writeValue = String(value || "").trim();
  if (!writeKey) {
    throw new Error("setManifestValue_: key is empty.");
  }

  var sheet = layout.sheet;
  var width = Math.max(layout.keyColumn, layout.valueColumn);
  var lastRow = sheet.getLastRow();
  if (lastRow >= 2) {
    var values = sheet.getRange(2, 1, lastRow - 1, width).getValues();
    for (var i = 0; i < values.length; i++) {
      var rowKey = String(values[i][layout.keyColumn - 1] || "").trim();
      if (rowKey === writeKey) {
        var rowIndex = i + 2;
        var currentValue = String(
          values[i][layout.valueColumn - 1] || "",
        ).trim();
        if (currentValue !== writeValue) {
          sheet.getRange(rowIndex, layout.valueColumn).setValue(writeValue);
        }
        return;
      }
    }
  }

  var appendRow = Math.max(2, lastRow + 1);
  if (appendRow > sheet.getMaxRows()) {
    sheet.insertRowsAfter(sheet.getMaxRows(), appendRow - sheet.getMaxRows());
  }

  var row = [];
  for (var c = 0; c < width; c++) {
    row.push("");
  }
  row[layout.keyColumn - 1] = writeKey;
  row[layout.valueColumn - 1] = writeValue;
  sheet.getRange(appendRow, 1, 1, width).setValues([row]);
}

function promptForSchemaUrl_() {
  var ui = SpreadsheetApp.getUi();
  var result = ui.prompt(
    "SparkPlug Schema URL",
    "Enter the schemaUrl for the schema JSON that contains sheetSpec:",
    ui.ButtonSet.OK_CANCEL,
  );

  if (result.getSelectedButton() !== ui.Button.OK) {
    throw new Error("Schema URL prompt was cancelled.");
  }

  var schemaUrl = String(result.getResponseText() || "").trim();
  if (!schemaUrl) {
    throw new Error("Schema URL is empty.");
  }

  setManifestValue_("schemaUrl", schemaUrl);
  return schemaUrl;
}

function fetchSchema_(schemaUrl) {
  var url = String(schemaUrl || "").trim();
  if (!url) {
    throw new Error("fetchSchema_: schemaUrl is empty.");
  }

  var response = UrlFetchApp.fetch(url, {
    followRedirects: true,
    muteHttpExceptions: true,
  });
  var responseCode = response.getResponseCode();
  if (responseCode !== 200) {
    throw new Error("fetchSchema_: HTTP " + responseCode + " for " + url);
  }

  try {
    return JSON.parse(response.getContentText());
  } catch (err) {
    throw new Error("fetchSchema_: invalid JSON. " + err.message);
  }
}

function getSheetSpec_(schema) {
  if (!schema || typeof schema !== "object") {
    throw new Error("getSheetSpec_: schema is missing or invalid.");
  }

  if (!schema.sheetSpec || typeof schema.sheetSpec !== "object") {
    throw new Error("getSheetSpec_: schema.sheetSpec is missing.");
  }

  if (!Array.isArray(schema.sheetSpec.tables)) {
    throw new Error("getSheetSpec_: schema.sheetSpec.tables must be an array.");
  }

  return schema.sheetSpec;
}

function getOrCreateSheet_(name) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sheet = ss.getSheetByName(name);
  if (sheet) {
    return sheet;
  }

  return ss.insertSheet(name);
}

function readHeader_(sheet, headerRowIndex) {
  var lastColumn = sheet.getLastColumn();
  if (lastColumn < 1) {
    return [];
  }

  var values = sheet.getRange(headerRowIndex, 1, 1, lastColumn).getValues()[0];
  var header = [];
  for (var i = 0; i < values.length; i++) {
    header.push(String(values[i] || "").trim());
  }

  return header;
}

function writeHeader_(sheet, headerRowIndex, headers) {
  if (!headers || headers.length === 0) {
    return;
  }

  ensureSheetColumnCapacity_(sheet, headers.length);
  var headerRange = sheet.getRange(headerRowIndex, 1, 1, headers.length);
  headerRange.setValues([headers]);
  styleHeaderRange_(headerRange);
}

function buildEnumTable_(sheetSpec) {
  var enumKeys = [];
  var columns = [];
  var enumsRoot = sheetSpec && sheetSpec.enums;

  if (!enumsRoot || typeof enumsRoot !== "object") {
    return { enumKeys: enumKeys, columns: columns };
  }

  flattenEnumNode_(enumsRoot, "enums", enumKeys, columns);
  return { enumKeys: enumKeys, columns: columns };
}

function getDropdownRangeForEnum_(dropdownSheet, enumColumnIndex) {
  var lastRow = dropdownSheet.getLastRow();
  if (lastRow < 2) {
    return null;
  }

  var values = dropdownSheet
    .getRange(2, enumColumnIndex, lastRow - 1, 1)
    .getDisplayValues();
  var lastNonEmptyOffset = 0;

  for (var i = 0; i < values.length; i++) {
    if (String(values[i][0] || "").trim() !== "") {
      lastNonEmptyOffset = i + 1;
    }
  }

  if (lastNonEmptyOffset === 0) {
    return null;
  }

  return dropdownSheet.getRange(2, enumColumnIndex, lastNonEmptyOffset, 1);
}

function loadSheetSpecContext_(promptIfMissing) {
  var schemaUrl = getManifestValue_("schemaUrl");
  if (!schemaUrl && promptIfMissing) {
    schemaUrl = promptForSchemaUrl_();
  }

  if (!schemaUrl) {
    throw new Error(
      "Manifest.schemaUrl is missing. Add it to Manifest or run again and provide it when prompted.",
    );
  }

  var schema = fetchSchema_(schemaUrl);
  var sheetSpec = getSheetSpec_(schema);
  var settings = getSheetSpecSettings_(sheetSpec);

  return {
    schemaUrl: schemaUrl,
    schema: schema,
    sheetSpec: sheetSpec,
    settings: settings,
  };
}

function getSheetSpecSettings_(sheetSpec) {
  var rawSettings = (sheetSpec && sheetSpec.settings) || {};
  var headerRowIndex = parseInt(rawSettings.headerRowIndex, 10);
  if (!isFinite(headerRowIndex) || headerRowIndex < 1) {
    headerRowIndex = 1;
  }

  var newColumnsPolicy = String(
    rawSettings.newColumnsPolicy || "append",
  ).trim();
  if (newColumnsPolicy !== "append") {
    throw new Error(
      'sheetSpec.settings.newColumnsPolicy must be "append". Found "' +
        newColumnsPolicy +
        '".',
    );
  }

  return {
    headerRowIndex: headerRowIndex,
    freezeHeaderRow: rawSettings.freezeHeaderRow !== false,
    allowExtraColumns: rawSettings.allowExtraColumns !== false,
    newColumnsPolicy: newColumnsPolicy,
  };
}

function ensureTableFromSheetSpec_(tableSpec, settings) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var tableName = String((tableSpec && tableSpec.name) || "").trim();
  if (!tableName) {
    throw new Error("ensureTableFromSheetSpec_: table name is empty.");
  }

  var sheet = ss.getSheetByName(tableName);
  var createdSheet = false;
  if (!sheet) {
    sheet = ss.insertSheet(tableName);
    createdSheet = true;
  }

  var declaredColumns = getDeclaredColumns_(tableSpec);
  var header = readHeader_(sheet, settings.headerRowIndex);
  var headerInfo = analyzeHeader_(header);
  var addedColumns = [];

  if (headerInfo.lastUsedColumn === 0) {
    var orderedHeaders = [];
    for (var i = 0; i < declaredColumns.length; i++) {
      orderedHeaders.push(declaredColumns[i].name);
    }

    if (orderedHeaders.length > 0) {
      writeHeader_(sheet, settings.headerRowIndex, orderedHeaders);
      addedColumns = orderedHeaders.slice();
    }
  } else {
    var toAppend = [];
    for (var c = 0; c < declaredColumns.length; c++) {
      var declared = declaredColumns[c];
      if (!headerInfo.indexByNormalized[normalizeHeaderName_(declared.name)]) {
        toAppend.push(declared.name);
      }
    }

    if (toAppend.length > 0) {
      var startColumn = headerInfo.lastUsedColumn + 1;
      ensureSheetColumnCapacity_(sheet, startColumn + toAppend.length - 1);
      var appendedHeaderRange = sheet.getRange(
        settings.headerRowIndex,
        startColumn,
        1,
        toAppend.length,
      );
      appendedHeaderRange.setValues([toAppend]);
      styleHeaderRange_(appendedHeaderRange);
      addedColumns = toAppend;
    }
  }

  var frozeHeaderRow = false;
  if (
    settings.freezeHeaderRow &&
    sheet.getFrozenRows() !== settings.headerRowIndex
  ) {
    sheet.setFrozenRows(settings.headerRowIndex);
    frozeHeaderRow = true;
  }

  return {
    createdSheet: createdSheet,
    addedColumns: addedColumns,
    frozeHeaderRow: frozeHeaderRow,
  };
}

function rewriteDropdownSheet_(dropdownSheet, enumTable) {
  dropdownSheet.clear();

  var enumKeys = enumTable.enumKeys || [];
  var columns = enumTable.columns || [];
  if (enumKeys.length === 0) {
    dropdownSheet.setFrozenRows(1);
    return {
      enumCount: 0,
      optionCount: 0,
    };
  }

  var maxValues = 0;
  for (var i = 0; i < columns.length; i++) {
    maxValues = Math.max(maxValues, columns[i].length);
  }

  var rowCount = Math.max(1, maxValues + 1);
  var columnCount = enumKeys.length;
  ensureSheetRowCapacity_(dropdownSheet, rowCount);
  ensureSheetColumnCapacity_(dropdownSheet, columnCount);

  var matrix = [];
  for (var r = 0; r < rowCount; r++) {
    var row = [];
    for (var c = 0; c < columnCount; c++) {
      row.push("");
    }
    matrix.push(row);
  }

  for (i = 0; i < enumKeys.length; i++) {
    matrix[0][i] = enumKeys[i];
    for (r = 0; r < columns[i].length; r++) {
      matrix[r + 1][i] = columns[i][r];
    }
  }

  var dropdownRange = dropdownSheet.getRange(1, 1, rowCount, columnCount);
  dropdownRange.setValues(matrix);
  styleHeaderRange_(dropdownSheet.getRange(1, 1, 1, columnCount));
  dropdownSheet.setFrozenRows(1);
  dropdownSheet.autoResizeColumns(1, columnCount);
  SpreadsheetApp.flush();

  for (i = 0; i < columnCount; i++) {
    var columnIndex = i + 1;
    dropdownSheet.setColumnWidth(
      columnIndex,
      dropdownSheet.getColumnWidth(columnIndex) + 25,
    );
  }

  return {
    enumCount: enumKeys.length,
    optionCount: maxValues,
  };
}

function styleHeaderRange_(range) {
  range
    .setFontColor(SPARKPLUG_HEADER_FONT_COLOR_)
    .setBackground(SPARKPLUG_HEADER_BACKGROUND_COLOR_);
}

function getDeclaredColumns_(tableSpec) {
  var columns = Array.isArray(tableSpec.columns) ? tableSpec.columns : [];
  var result = [];
  var normalizedNameSet = {};

  for (var i = 0; i < columns.length; i++) {
    var columnSpec = columns[i] || {};
    var name = String(columnSpec.name || "").trim();
    if (!name) {
      continue;
    }

    result.push({
      name: name,
      required: columnSpec.required === true,
      type: String(columnSpec.type || "").trim(),
      enumRef: String(columnSpec.enumRef || "").trim(),
    });
    normalizedNameSet[normalizeHeaderName_(name)] = true;
  }

  result.normalizedNameSet = normalizedNameSet;
  return result;
}

function analyzeHeader_(header) {
  var trimmedHeader = header || [];
  var lastUsedColumn = 0;
  var indexByNormalized = {};
  var duplicates = [];
  var blankColumnIndexes = [];

  for (var i = 0; i < trimmedHeader.length; i++) {
    if (String(trimmedHeader[i] || "").trim() !== "") {
      lastUsedColumn = i + 1;
    }
  }

  var headersWithinUsedRange = trimmedHeader.slice(0, lastUsedColumn);
  for (i = 0; i < headersWithinUsedRange.length; i++) {
    var headerName = String(headersWithinUsedRange[i] || "").trim();
    if (!headerName) {
      blankColumnIndexes.push(i + 1);
      continue;
    }

    var normalized = normalizeHeaderName_(headerName);
    if (indexByNormalized[normalized]) {
      duplicates.push(headerName);
      continue;
    }

    indexByNormalized[normalized] = i + 1;
  }

  return {
    lastUsedColumn: lastUsedColumn,
    headersWithinUsedRange: headersWithinUsedRange,
    indexByNormalized: indexByNormalized,
    duplicates: duplicates,
    blankColumnIndexes: blankColumnIndexes,
  };
}

function normalizeHeaderName_(value) {
  return String(value || "")
    .trim()
    .toLowerCase();
}

function resolveEnumKey_(enumRef, enumColumnIndexByKey) {
  var key = String(enumRef || "").trim();
  if (!key) {
    return null;
  }

  if (enumColumnIndexByKey[key]) {
    return key;
  }

  if (key.indexOf("enums.") === 0) {
    var stripped = key.substring(6);
    if (enumColumnIndexByKey[stripped]) {
      return stripped;
    }
  } else {
    var prefixed = "enums." + key;
    if (enumColumnIndexByKey[prefixed]) {
      return prefixed;
    }
  }

  return null;
}

function flattenEnumNode_(node, prefix, enumKeys, columns) {
  if (Array.isArray(node)) {
    var values = [];
    for (var i = 0; i < node.length; i++) {
      var value = String(node[i] || "").trim();
      if (value !== "") {
        values.push(value);
      }
    }

    enumKeys.push(prefix);
    columns.push(values);
    return;
  }

  if (!node || typeof node !== "object") {
    return;
  }

  var keys = Object.keys(node);
  for (var k = 0; k < keys.length; k++) {
    var key = keys[k];
    var nextPrefix = prefix ? prefix + "." + key : key;
    flattenEnumNode_(node[key], nextPrefix, enumKeys, columns);
  }
}

function getManifestLayout_() {
  var sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName("Manifest");
  if (!sheet) {
    throw new Error(
      'Manifest sheet is missing. Create a "Manifest" sheet with "key" and "value" headers.',
    );
  }

  var header = readHeader_(sheet, 1);
  var keyColumn = 0;
  var valueColumn = 0;
  for (var i = 0; i < header.length; i++) {
    var normalized = normalizeHeaderName_(header[i]);
    if (normalized === "key" && !keyColumn) {
      keyColumn = i + 1;
    } else if (normalized === "value" && !valueColumn) {
      valueColumn = i + 1;
    }
  }

  if (!keyColumn || !valueColumn) {
    throw new Error('Manifest must have "key" and "value" headers in row 1.');
  }

  return {
    sheet: sheet,
    keyColumn: keyColumn,
    valueColumn: valueColumn,
  };
}

function ensureSheetRowCapacity_(sheet, requiredRows) {
  var maxRows = sheet.getMaxRows();
  if (maxRows < requiredRows) {
    sheet.insertRowsAfter(maxRows, requiredRows - maxRows);
  }
}

function ensureSheetColumnCapacity_(sheet, requiredColumns) {
  var maxColumns = sheet.getMaxColumns();
  if (maxColumns < requiredColumns) {
    sheet.insertColumnsAfter(maxColumns, requiredColumns - maxColumns);
  }
}

function buildEnsureTabsSummary_(report) {
  var lines = [];
  var tabNames = Object.keys(report.addedColumnsByTab);

  lines.push("Ensure Tabs & Headers complete.");
  lines.push("Created tabs: " + formatListOrNone_(report.createdTabs));
  if (tabNames.length === 0) {
    lines.push("Added columns: none");
  } else {
    lines.push("Added columns:");
    for (var i = 0; i < tabNames.length; i++) {
      var tabName = tabNames[i];
      lines.push(
        "- " + tabName + ": " + report.addedColumnsByTab[tabName].join(", "),
      );
    }
  }
  lines.push("Header row frozen on: " + formatListOrNone_(report.frozenTabs));

  if (report.skippedOptionalTabs.length > 0) {
    lines.push(
      "Optional sheets not present and left untouched: " +
        report.skippedOptionalTabs.join(", "),
    );
  }

  return lines.join("\n");
}

function buildRefreshDropdownsSummary_(rewriteInfo, validationReport) {
  var lines = [];
  lines.push("Refresh Dropdowns complete.");
  lines.push(
    "Dropdowns rewritten with " +
      rewriteInfo.enumCount +
      " enum columns and up to " +
      rewriteInfo.optionCount +
      " values per enum.",
  );
  lines.push("Validations applied: " + validationReport.validationsApplied);

  if (validationReport.examples.length > 0) {
    lines.push("Examples:");
    for (var i = 0; i < validationReport.examples.length; i++) {
      lines.push("- " + validationReport.examples[i]);
    }
  }

  if (validationReport.warnings.length > 0) {
    lines.push("Warnings:");
    for (i = 0; i < validationReport.warnings.length; i++) {
      lines.push("- " + validationReport.warnings[i]);
    }
  }

  return lines.join("\n");
}

function buildValidateStructureSummary_(report, totalTables) {
  var lines = [];
  lines.push("Validate Sheet Structure complete.");
  lines.push("Errors: " + report.errors.length);
  if (report.errors.length > 0) {
    for (var i = 0; i < report.errors.length; i++) {
      lines.push("- " + report.errors[i]);
    }
  }

  lines.push("Warnings: " + report.warnings.length);
  if (report.warnings.length > 0) {
    for (i = 0; i < report.warnings.length; i++) {
      lines.push("- " + report.warnings[i]);
    }
  }

  lines.push("OK tables: " + report.okTables.length + " / " + totalTables);
  if (report.okTables.length > 0) {
    lines.push("OK: " + report.okTables.join(", "));
  }

  return lines.join("\n");
}

function formatListOrNone_(items) {
  return items && items.length > 0 ? items.join(", ") : "none";
}

function handleSparkPlugError_(prefix, err) {
  var ui = SpreadsheetApp.getUi();
  var message = prefix + ": " + (err && err.message ? err.message : err);
  Logger.log(message);
  ui.alert("SparkPlug", message, ui.ButtonSet.OK);
}
