/**
 * Spark Plug Apps Script bootstrap.
 *
 * The sheet-spec tooling lives in spark_plug_sheet_tools.gs.
 * This file keeps a single onOpen() entry point for the Apps Script project.
 */

function onOpen() {
  SparkPlugSheetTools.installMenu();
}

// Delegates required because menu callbacks must exist in the container-bound project.
function ensureTabsAndHeadersFromSheetSpec() {
  SparkPlugSheetTools.ensureTabsAndHeadersFromSheetSpec();
}
function refreshDropdownsFromSheetSpec() {
  SparkPlugSheetTools.refreshDropdownsFromSheetSpec();
}
function validateSheetStructureFromSheetSpec() {
  SparkPlugSheetTools.validateSheetStructureFromSheetSpec();
}
