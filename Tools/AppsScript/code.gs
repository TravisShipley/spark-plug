/**
 * Spark Plug Apps Script bootstrap.
 *
 * The sheet-spec tooling lives in spark_plug_sheet_tools.gs.
 * This file keeps a single onOpen() entry point for the Apps Script project.
 */

function onOpen() {
  if (typeof addSparkPlugSheetToolsMenuItems_ !== "function") {
    return;
  }

  var menu = SpreadsheetApp.getUi().createMenu("SparkPlug");
  addSparkPlugSheetToolsMenuItems_(menu);
  menu.addToUi();
}
