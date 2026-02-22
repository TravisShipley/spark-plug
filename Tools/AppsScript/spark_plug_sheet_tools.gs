/**
 * Spark Plug sheet tools:
 * - Reads schema.sheetSpec from schemaUrl in PackMeta
 * - Ensures tabs and headers exist without modifying data rows
 */

function onOpen() {
  var ui = SpreadsheetApp.getUi();
  var menu = ui.createMenu('SparkPlug');
  menu.addItem(
    'Ensure Tabs & Headers (from sheetSpec)',
    'ensureTabsAndHeadersFromSheetSpec'
  );
  menu.addToUi();
}

function ensureTabsAndHeadersFromSheetSpec() {
  var ui = SpreadsheetApp.getUi();
  var report = {
    createdTabs: [],
    addedColumnsByTab: {},
    requiredTabsCreated: [],
    requiredColumnsAddedByTab: {},
    warnings: []
  };

  try {
    var schemaUrl = getPackMetaValue_('schemaUrl');
    if (!schemaUrl) {
      var prompt = ui.prompt(
        'Schema URL missing',
        'Enter schemaUrl (raw JSON URL) to read sheetSpec:',
        ui.ButtonSet.OK_CANCEL
      );

      if (prompt.getSelectedButton() !== ui.Button.OK) {
        var cancelled = 'Ensure Tabs & Headers cancelled: schemaUrl not provided.';
        Logger.log(cancelled);
        ui.alert(cancelled);
        return;
      }

      schemaUrl = (prompt.getResponseText() || '').trim();
      if (!schemaUrl) {
        throw new Error('schemaUrl is empty.');
      }

      setPackMetaValue_('schemaUrl', schemaUrl);
      report.warnings.push('schemaUrl was missing in PackMeta and was added.');
    }

    var schema = fetchSchema_(schemaUrl);
    var sheetSpec = getSheetSpec_(schema);
    var settings = sheetSpec.settings || {};

    var headerRowIndex = Number(settings.headerRowIndex);
    if (!isFinite(headerRowIndex) || headerRowIndex < 1) {
      headerRowIndex = 1;
    } else {
      headerRowIndex = Math.floor(headerRowIndex);
    }

    var freezeHeaderRow = settings.freezeHeaderRow !== false;
    var allowExtraColumns = settings.allowExtraColumns !== false;
    var newColumnsPolicy = String(settings.newColumnsPolicy || 'append').trim();
    if (newColumnsPolicy !== 'append') {
      throw new Error(
        'sheetSpec.settings.newColumnsPolicy must be "append". Received: "' +
          newColumnsPolicy +
          '".'
      );
    }

    if (!allowExtraColumns) {
      report.warnings.push(
        'allowExtraColumns is false in sheetSpec; this tool still does not delete columns.'
      );
    }

    var packMetaSchemaVersion = getPackMetaValue_('schemaVersion');
    var expectedVersion = String(sheetSpec.version || schema.version || '').trim();
    if (
      packMetaSchemaVersion &&
      expectedVersion &&
      String(packMetaSchemaVersion).trim() !== expectedVersion
    ) {
      report.warnings.push(
        'PackMeta schemaVersion (' +
          packMetaSchemaVersion +
          ') does not match schema sheetSpec/version (' +
          expectedVersion +
          ').'
      );
    }

    var tables = Array.isArray(sheetSpec.tables) ? sheetSpec.tables : [];
    for (var i = 0; i < tables.length; i++) {
      var tableSpec = tables[i];
      if (!tableSpec || !tableSpec.name) {
        continue;
      }

      var sheetName = String(tableSpec.name).trim();
      if (!sheetName) {
        continue;
      }

      var existing = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(sheetName);
      var shouldEnsure = tableSpec.required === true || !!existing;
      if (!shouldEnsure) {
        continue;
      }

      var perTableSettings = {
        headerRowIndex: headerRowIndex,
        freezeHeaderRow: freezeHeaderRow,
        allowExtraColumns: allowExtraColumns,
        newColumnsPolicy: newColumnsPolicy
      };

      var result = ensureTable_(tableSpec, perTableSettings);
      if (result.createdSheet) {
        report.createdTabs.push(sheetName);
        if (tableSpec.required === true) {
          report.requiredTabsCreated.push(sheetName);
        }
      }

      if (result.addedColumns.length > 0) {
        report.addedColumnsByTab[sheetName] = result.addedColumns;
      }

      if (result.addedRequiredColumns.length > 0) {
        report.requiredColumnsAddedByTab[sheetName] = result.addedRequiredColumns;
      }
    }

    var summary = buildSummary_(report);
    Logger.log(summary);
    ui.alert(summary);
  } catch (err) {
    var message = 'Ensure Tabs & Headers failed: ' + (err && err.message ? err.message : err);
    Logger.log(message);
    ui.alert(message);
  }
}

function fetchSchema_(schemaUrl) {
  var url = String(schemaUrl || '').trim();
  if (!url) {
    throw new Error('fetchSchema_: schemaUrl is empty.');
  }

  var response = UrlFetchApp.fetch(url, { muteHttpExceptions: true });
  var code = response.getResponseCode();
  if (code !== 200) {
    throw new Error('fetchSchema_: HTTP ' + code + ' for URL: ' + url);
  }

  var body = response.getContentText();
  try {
    return JSON.parse(body);
  } catch (err) {
    throw new Error('fetchSchema_: schema JSON parse failed. ' + err.message);
  }
}

function getSheetSpec_(schema) {
  if (!schema || typeof schema !== 'object') {
    throw new Error('getSheetSpec_: schema is missing or invalid.');
  }

  if (!schema.sheetSpec || typeof schema.sheetSpec !== 'object') {
    throw new Error('getSheetSpec_: schema.sheetSpec is missing.');
  }

  if (!Array.isArray(schema.sheetSpec.tables)) {
    throw new Error('getSheetSpec_: schema.sheetSpec.tables must be an array.');
  }

  return schema.sheetSpec;
}

function getPackMetaValue_(key) {
  var lookupKey = String(key || '').trim();
  if (!lookupKey) {
    return null;
  }

  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sheet = ss.getSheetByName('PackMeta');
  if (!sheet) {
    return null;
  }

  var header = readPackMetaHeader_(sheet);
  var keyCol = header.keyCol;
  var valueCol = header.valueCol;

  var lastRow = sheet.getLastRow();
  var width = Math.max(sheet.getLastColumn(), Math.max(keyCol, valueCol));
  if (lastRow <= 1 || width <= 0) {
    return null;
  }

  var values = sheet.getRange(2, 1, lastRow - 1, width).getValues();
  for (var i = 0; i < values.length; i++) {
    var row = values[i];
    var rowKey = String(row[keyCol - 1] || '').trim();
    if (rowKey === lookupKey) {
      var value = String(row[valueCol - 1] || '').trim();
      return value || null;
    }
  }

  return null;
}

function setPackMetaValue_(key, value) {
  var writeKey = String(key || '').trim();
  var writeValue = String(value || '').trim();
  if (!writeKey) {
    throw new Error('setPackMetaValue_: key is empty.');
  }

  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sheet = ss.getSheetByName('PackMeta');
  if (!sheet) {
    sheet = ss.insertSheet('PackMeta');
  }

  var header = readPackMetaHeader_(sheet);
  var keyCol = header.keyCol;
  var valueCol = header.valueCol;
  var width = Math.max(sheet.getLastColumn(), Math.max(keyCol, valueCol));

  var lastRow = sheet.getLastRow();
  if (lastRow >= 2) {
    var values = sheet.getRange(2, 1, lastRow - 1, width).getValues();
    for (var i = 0; i < values.length; i++) {
      var rowKey = String(values[i][keyCol - 1] || '').trim();
      if (rowKey === writeKey) {
        sheet.getRange(i + 2, valueCol).setValue(writeValue);
        return;
      }
    }
  }

  var appendAt = Math.max(2, lastRow + 1);
  if (sheet.getMaxRows() < appendAt) {
    sheet.insertRowsAfter(sheet.getMaxRows(), appendAt - sheet.getMaxRows());
  }

  var out = [];
  for (var c = 0; c < width; c++) {
    out.push('');
  }
  out[keyCol - 1] = writeKey;
  out[valueCol - 1] = writeValue;
  sheet.getRange(appendAt, 1, 1, width).setValues([out]);
}

function ensureTable_(tableSpec, settings) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var tableName = String(tableSpec.name || '').trim();
  if (!tableName) {
    throw new Error('ensureTable_: table name is empty.');
  }

  var headerRowIndex = Number(settings.headerRowIndex);
  if (!isFinite(headerRowIndex) || headerRowIndex < 1) {
    headerRowIndex = 1;
  } else {
    headerRowIndex = Math.floor(headerRowIndex);
  }

  var declaredColumns = Array.isArray(tableSpec.columns) ? tableSpec.columns : [];
  var normalizedColumns = [];
  for (var i = 0; i < declaredColumns.length; i++) {
    var col = declaredColumns[i] || {};
    var name = String(col.name || '').trim();
    if (!name) {
      continue;
    }
    normalizedColumns.push({
      name: name,
      required: col.required === true,
      type: String(col.type || '').trim()
    });
  }

  var createdSheet = false;
  var sheet = ss.getSheetByName(tableName);
  if (!sheet) {
    sheet = ss.insertSheet(tableName);
    createdSheet = true;
  }

  if (sheet.getMaxRows() < headerRowIndex) {
    sheet.insertRowsAfter(sheet.getMaxRows(), headerRowIndex - sheet.getMaxRows());
  }

  var currentLastCol = Math.max(sheet.getLastColumn(), 1);
  var headerRange = sheet.getRange(headerRowIndex, 1, 1, currentLastCol);
  var headerValues = headerRange.getValues()[0];

  var isHeaderBlank = true;
  for (var h = 0; h < headerValues.length; h++) {
    if (String(headerValues[h] || '').trim() !== '') {
      isHeaderBlank = false;
      break;
    }
  }

  var addedColumns = [];
  var addedRequiredColumns = [];

  if (isHeaderBlank) {
    var ordered = normalizedColumns.map(function(col) {
      return col.name;
    });

    if (ordered.length > 0) {
      sheet.getRange(headerRowIndex, 1, 1, ordered.length).setValues([ordered]);
      addedColumns = ordered.slice();
      for (var r = 0; r < normalizedColumns.length; r++) {
        if (normalizedColumns[r].required) {
          addedRequiredColumns.push(normalizedColumns[r].name);
        }
      }
    }
  } else {
    var existingHeaderSet = {};
    for (var e = 0; e < headerValues.length; e++) {
      var existingName = String(headerValues[e] || '').trim();
      if (!existingName) {
        continue;
      }
      existingHeaderSet[existingName] = true;
    }

    var toAppend = [];
    for (var cIdx = 0; cIdx < normalizedColumns.length; cIdx++) {
      var declared = normalizedColumns[cIdx];
      if (!existingHeaderSet[declared.name]) {
        toAppend.push(declared.name);
        existingHeaderSet[declared.name] = true;
        if (declared.required) {
          addedRequiredColumns.push(declared.name);
        }
      }
    }

    if (toAppend.length > 0) {
      var startCol = Math.max(sheet.getLastColumn(), 1) + 1;
      sheet.getRange(headerRowIndex, startCol, 1, toAppend.length).setValues([toAppend]);
      addedColumns = toAppend;
    }
  }

  if (settings.freezeHeaderRow !== false) {
    sheet.setFrozenRows(headerRowIndex);
  }

  return {
    createdSheet: createdSheet,
    addedColumns: addedColumns,
    addedRequiredColumns: addedRequiredColumns
  };
}

function readPackMetaHeader_(sheet) {
  var minWidth = Math.max(sheet.getLastColumn(), 2);
  var headerValues = sheet.getRange(1, 1, 1, minWidth).getValues()[0];

  var keyCol = -1;
  var valueCol = -1;
  for (var i = 0; i < headerValues.length; i++) {
    var header = String(headerValues[i] || '').trim();
    if (header === 'key') {
      keyCol = i + 1;
    } else if (header === 'value') {
      valueCol = i + 1;
    }
  }

  if (keyCol <= 0) {
    keyCol = Math.max(sheet.getLastColumn(), 0) + 1;
    sheet.getRange(1, keyCol).setValue('key');
  }

  if (valueCol <= 0) {
    valueCol = Math.max(sheet.getLastColumn(), 0) + 1;
    sheet.getRange(1, valueCol).setValue('value');
  }

  return { keyCol: keyCol, valueCol: valueCol };
}

function buildSummary_(report) {
  var lines = [];
  lines.push('Ensure Tabs & Headers (from sheetSpec) complete.');
  lines.push('');

  if (report.createdTabs.length > 0) {
    lines.push('Created tabs:');
    for (var i = 0; i < report.createdTabs.length; i++) {
      lines.push('- ' + report.createdTabs[i]);
    }
  } else {
    lines.push('Created tabs: none');
  }

  lines.push('');
  var tabNames = Object.keys(report.addedColumnsByTab);
  if (tabNames.length > 0) {
    lines.push('Added columns by tab:');
    for (var t = 0; t < tabNames.length; t++) {
      var tab = tabNames[t];
      lines.push('- ' + tab + ': ' + report.addedColumnsByTab[tab].join(', '));
    }
  } else {
    lines.push('Added columns by tab: none');
  }

  lines.push('');
  if (report.requiredTabsCreated.length > 0) {
    lines.push('Required tabs created: ' + report.requiredTabsCreated.join(', '));
  } else {
    lines.push('Required tabs created: none');
  }

  var reqTabNames = Object.keys(report.requiredColumnsAddedByTab);
  if (reqTabNames.length > 0) {
    lines.push('Required columns added:');
    for (var r = 0; r < reqTabNames.length; r++) {
      var reqTab = reqTabNames[r];
      lines.push(
        '- ' + reqTab + ': ' + report.requiredColumnsAddedByTab[reqTab].join(', ')
      );
    }
  } else {
    lines.push('Required columns added: none');
  }

  if (report.warnings.length > 0) {
    lines.push('');
    lines.push('Warnings:');
    for (var w = 0; w < report.warnings.length; w++) {
      lines.push('- ' + report.warnings[w]);
    }
  }

  return lines.join('\n');
}
