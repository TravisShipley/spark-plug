// Assets/Editor/GoogleSheetImporter.cs
//
// SparkPlug - Google Sheet Importer (Index-driven, Sheets API v4)
//
// Rules:
// - Config needs spreadsheetId (can be full URL or ID) and apiKey
// - Import list is defined by __Index sheet
// - __Index defines: sheet, enabled, order (column names are required and case-insensitive)
// - Sheet names must be unique (throws error if duplicates found)
// - Tables listed in __Index are imported as-is
// - Commented-out sheets (starting with "__" or "//") are ignored
// - Commented-out rows (first cell starts with "_" or "//" or blank) are ignored
//
// Notes:
// - Requires Google API key for Sheets API v4
// - Uses spreadsheet metadata to find sheets by name
// - Fetches data via Sheets API values endpoint

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

public static class GoogleSheetImporter
{
    private const string DataTextPath = "Assets/Data/data.txt";
    private const string SchemaPath = "Assets/Data/spark_plug_definition_schema.json";
    private const string SheetSpecPath = "Assets/Data/spark_plug_sheet_spec.json";
    private const string CsvAiBundlePath = "Assets/Data/Csv/_sheets_export_for_ai.json";
    private const string DataAddressableGroupName = "Data";
    private const string DefaultDefinitionsDirectory = "Assets/Data/Definitions";
    private const string IndexSheetName = "__Index";
    private const int MaxSchemaValidationErrors = 25;

    private sealed class SheetSpecColumn
    {
        public string Name;
        public bool Required;
        public string Type;
    }

    private sealed class SheetSpecRename
    {
        public string From;
        public string To;
    }

    private sealed class SheetSpecEmbed
    {
        public string ParentTable;
        public string ParentIdColumn;
        public string ForeignKeyColumn;
        public string As;
    }

    private sealed class SheetSpecTable
    {
        public string Name;
        public string PackKey;
        public string Emit;
        public string RowMode;
        public bool Required;
        public bool IsVirtual;
        public string PrimaryKey;
        public readonly List<SheetSpecColumn> Columns = new();
        public readonly List<SheetSpecRename> ColumnRenames = new();
        public readonly Dictionary<string, SheetSpecColumn> ColumnsByName = new(
            StringComparer.OrdinalIgnoreCase
        );
        public readonly Dictionary<string, string> RenamedColumnsByFrom = new(
            StringComparer.OrdinalIgnoreCase
        );
        public SheetSpecEmbed Embed;
    }

    private sealed class SheetSpecDefinition
    {
        private readonly List<SheetSpecTable> tablesInOrder = new();
        private readonly Dictionary<string, SheetSpecTable> byName = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly HashSet<string> reservedSheets = new(StringComparer.OrdinalIgnoreCase);

        public bool AllowExtraColumns { get; set; } = true;
        public IReadOnlyList<SheetSpecTable> TablesInOrder => tablesInOrder;

        public void AddReservedSheet(string sheetName)
        {
            if (string.IsNullOrWhiteSpace(sheetName))
                return;

            reservedSheets.Add(sheetName.Trim());
        }

        public bool IsReservedSheet(string sheetName)
        {
            if (string.IsNullOrWhiteSpace(sheetName))
                return false;

            return reservedSheets.Contains(sheetName.Trim());
        }

        public bool ContainsTable(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return false;

            return byName.ContainsKey(tableName.Trim());
        }

        public void Add(SheetSpecTable table)
        {
            if (table == null || string.IsNullOrWhiteSpace(table.Name))
                return;

            if (byName.ContainsKey(table.Name))
            {
                throw new InvalidOperationException(
                    $"Sheet spec has duplicate table name '{table.Name}'."
                );
            }

            tablesInOrder.Add(table);
            byName[table.Name] = table;
        }

        public bool IsRequired(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return false;

            return byName.TryGetValue(tableName.Trim(), out var table) && table.Required;
        }

        public bool TryGet(string tableName, out SheetSpecTable table)
        {
            table = null;
            if (string.IsNullOrWhiteSpace(tableName))
                return false;

            return byName.TryGetValue(tableName.Trim(), out table);
        }

        public IEnumerable<string> RequiredNonVirtualTableNames =>
            tablesInOrder.Where(t => t.Required && !t.IsVirtual).Select(t => t.Name);
    }

    private sealed class ParsedTable
    {
        public SheetSpecTable Definition;
        public List<string> Header = new();
        public List<Dictionary<string, object>> Rows = new();
    }

    private sealed class RawSheetSnapshot
    {
        public string Name;
        public string Gid;
        public int Order;
        public bool IsIndex;
        public string Csv;
    }

    [MenuItem("SparkPlug/Import From Google Sheet")]
    public static void Import()
    {
        var config = ResolveSingleImportConfig();
        if (config == null)
            return;

        Import(config);
    }

    [MenuItem("SparkPlug/Import Selected Definition")]
    public static void ImportSelected()
    {
        var config = Selection.activeObject as GoogleSheetImportConfig;
        if (config == null)
        {
            EditorUtility.DisplayDialog(
                "Import Failed",
                "Select a GoogleSheetImportConfig asset, then run Import Selected Definition.",
                "OK"
            );
            return;
        }

        Import(config);
    }

    [MenuItem("SparkPlug/Import All Definitions")]
    public static void ImportAll()
    {
        var configs = FindConfigs();
        if (configs.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Import Failed",
                "No GoogleSheetImportConfig assets found. Create one via Assets > Create > SparkPlug > Google Sheet Import Config.",
                "OK"
            );
            return;
        }

        Debug.ClearDeveloperConsole();

        try
        {
            for (int i = 0; i < configs.Count; i++)
            {
                var config = configs[i];
                EditorUtility.DisplayProgressBar(
                    "SparkPlug Import",
                    $"Importing definition {i + 1}/{configs.Count}: {config.name}",
                    configs.Count <= 0 ? 0f : (float)i / configs.Count
                );
                ImportInternal(config);
            }
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog(
                "Import Failed",
                ex.Message + "\n\nSee Console for details.",
                "OK"
            );
            Debug.LogException(ex);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void Import(GoogleSheetImportConfig config)
    {
        Debug.ClearDeveloperConsole();

        try
        {
            ImportInternal(config);
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog(
                "Import Failed",
                ex.Message + "\n\nSee Console for details.",
                "OK"
            );
            Debug.LogException(ex);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void ImportInternal(GoogleSheetImportConfig config)
    {
        if (config == null)
        {
            EditorUtility.DisplayDialog(
                "Import Failed",
                "No GoogleSheetImportConfig was provided.",
                "OK"
            );
            return;
        }

        var spreadsheetId = NormalizeSpreadsheetId(config.spreadsheetId);
        if (string.IsNullOrWhiteSpace(spreadsheetId))
        {
            EditorUtility.DisplayDialog(
                "Import Failed",
                "Config has no spreadsheetId. Set it to the spreadsheet ID or full URL.",
                "OK"
            );
            return;
        }

        if (string.IsNullOrWhiteSpace(config.apiKey))
        {
            EditorUtility.DisplayDialog(
                "Import Failed",
                "Config has no API key. Set it to your Google Sheets API key.",
                "OK"
            );
            return;
        }

        var outputJsonPath = NormalizeOutputJsonPath(config.outputJsonPath);
        if (string.IsNullOrWhiteSpace(outputJsonPath))
        {
            EditorUtility.DisplayDialog(
                "Import Failed",
                $"Config '{config.name}' has no valid outputJsonPath. Use a path under '{DefaultDefinitionsDirectory}/'.",
                "OK"
            );
            return;
        }

        var resourcesFallbackOutputPath = NormalizeOptionalAssetPath(config.resourcesFallbackOutputPath);
        var addressableKey = NormalizeOptionalValue(config.addressableKey);

        var sheetSpec = LoadSheetSpecDefinition();
        var rawSheetSnapshots = new List<RawSheetSnapshot>();
        var tables = FetchAllTables(spreadsheetId, config.apiKey, sheetSpec, rawSheetSnapshots);
        PersistRawSheetExports(spreadsheetId, rawSheetSnapshots);
        if (tables.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Import Failed",
                "No tables were imported from __Index entries.\n\n"
                    + "Ensure the __Index sheet exists and your API key is valid.",
                "OK"
            );
            return;
        }

        var importedTableNames = tables
            .Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var importedTableSummary = importedTableNames
            .Select(name => $"{name}({tables[name].Rows.Count})")
            .ToList();
        Debug.Log(
            $"[GoogleSheetImporter] Imported {importedTableNames.Count} table(s) for '{config.name}': {string.Join(", ", importedTableSummary)}"
        );

        var pack = BuildPackFromSpec(tables, sheetSpec);
        ValidatePack(pack);
        WriteJson(pack, outputJsonPath, resourcesFallbackOutputPath);
        AssetDatabase.Refresh();

        if (!string.IsNullOrWhiteSpace(addressableKey))
        {
            EnsureGameDefinitionAddressable(outputJsonPath, addressableKey, DataAddressableGroupName);
            if (config.rebuildAddressables)
                BuildAddressablesPlayerContent();
        }

        Debug.Log($"<color=green>✔ Import Complete</color>\nWrote {outputJsonPath}");
    }

    // ----------------------------
    // Config / ID helpers
    // ----------------------------

    private static List<GoogleSheetImportConfig> FindConfigs()
    {
        var guids = AssetDatabase.FindAssets("t:GoogleSheetImportConfig");
        var configs = new List<GoogleSheetImportConfig>();
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var config = AssetDatabase.LoadAssetAtPath<GoogleSheetImportConfig>(path);
            if (config != null)
                configs.Add(config);
        }

        configs.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        return configs;
    }

    private static GoogleSheetImportConfig ResolveSingleImportConfig()
    {
        if (Selection.activeObject is GoogleSheetImportConfig selectedConfig)
            return selectedConfig;

        var configs = FindConfigs();
        if (configs.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Import Failed",
                "No GoogleSheetImportConfig assets found. Create one via Assets > Create > SparkPlug > Google Sheet Import Config.",
                "OK"
            );
            return null;
        }

        if (configs.Count == 1)
            return configs[0];

        EditorUtility.DisplayDialog(
            "Import Failed",
            "Multiple GoogleSheetImportConfig assets were found. Select one and use Import Selected Definition, or use Import All Definitions.",
            "OK"
        );
        return null;
    }

    // Accepts:
    // - raw ID: 1ILzPwrsXiR3u724Fs3OnUHCKHM-hJ8EY60Ukcs1hHZs
    // - full URL: https://docs.google.com/spreadsheets/d/<id>/edit...
    // - published URL: https://docs.google.com/spreadsheets/d/e/<token>/pubhtml...
    //
    // Returns:
    // - "<id>" for normal sheets
    // - "<id>" for published sheets (extracts the base ID)
    private static string NormalizeSpreadsheetId(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        input = input.Trim();

        if (
            input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
        {
            var uri = new Uri(input);
            var parts = uri.AbsolutePath.Split(
                new[] { '/' },
                StringSplitOptions.RemoveEmptyEntries
            );

            // /spreadsheets/d/<id>/...
            var dIndex = Array.IndexOf(parts, "d");
            if (dIndex >= 0 && dIndex + 1 < parts.Length)
            {
                var id = parts[dIndex + 1];
                if (!string.IsNullOrEmpty(id) && id != "e")
                    return id;

                // /spreadsheets/d/e/<id>/... (published form - extract the actual ID)
                if (id == "e" && dIndex + 2 < parts.Length)
                    return parts[dIndex + 2];
            }

            return input; // fallback
        }

        return input;
    }

    private static string BuildSheetMetadataUrl(string spreadsheetId, string apiKey)
    {
        return $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}?key={Uri.EscapeDataString(apiKey)}";
    }

    private static string BuildValuesUrl(string spreadsheetId, string range, string apiKey)
    {
        return $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}/values/{Uri.EscapeDataString(range)}?key={Uri.EscapeDataString(apiKey)}";
    }

    private static string BuildCsvExportByGidUrl(string spreadsheetId, string gid)
    {
        return $"https://docs.google.com/spreadsheets/d/{Uri.EscapeDataString(spreadsheetId)}/export?format=csv&gid={Uri.EscapeDataString(gid ?? string.Empty)}";
    }

    // ----------------------------
    // Index-driven import (Sheets API v4)
    // ----------------------------

    private static Dictionary<string, ParsedTable> FetchAllTables(
        string spreadsheetId,
        string apiKey,
        SheetSpecDefinition sheetSpec,
        List<RawSheetSnapshot> rawSheetSnapshots
    )
    {
        // 1) Get spreadsheet metadata
        var metadata = GetSpreadsheetMetadata(spreadsheetId, apiKey);
        Debug.Log($"[GoogleSheetImporter] Fetched metadata for spreadsheet: {spreadsheetId}");

        // 2) Validate no duplicate sheet names
        ValidateNoDuplicateSheetNames(metadata);

        // 3) Find __Index sheet
        var indexSheet = FindSheetByName(metadata, IndexSheetName);
        if (indexSheet == null)
        {
            throw new InvalidOperationException(
                $"Could not find '{IndexSheetName}' sheet in spreadsheet metadata."
            );
        }

        // 4) Load index data
        var indexRange = $"'{IndexSheetName}'!A:Z";
        var indexValues = GetSheetValues(spreadsheetId, indexRange, apiKey);
        var indexCsv = ValuesToCsv(indexValues);
        AddRawSheetSnapshot(rawSheetSnapshots, IndexSheetName, string.Empty, -1, true, indexCsv);
        PersistRawSheetExports(spreadsheetId, rawSheetSnapshots);
        var index = ParseIndex(indexCsv);
        ValidateRequiredTablesInIndex(index, sheetSpec);

        // 5) Import tables in order
        var tables = new Dictionary<string, ParsedTable>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < index.Count; i++)
        {
            var entry = index[i];
            EditorUtility.DisplayProgressBar(
                "SparkPlug Import",
                $"Importing {entry.TableName} ({i + 1}/{index.Count})",
                index.Count == 0 ? 1f : (i + 1f) / index.Count
            );

            if (
                entry.TableName.StartsWith("_", StringComparison.Ordinal)
                || entry.TableName.StartsWith("//", StringComparison.Ordinal)
            )
                continue;

            if (
                sheetSpec.IsReservedSheet(entry.TableName)
                && !string.Equals(entry.TableName, IndexSheetName, StringComparison.Ordinal)
                && !sheetSpec.ContainsTable(entry.TableName)
            )
            {
                Debug.Log(
                    $"[GoogleSheetImporter] Reserved sheet '{entry.TableName}' is enabled in __Index. Ignoring."
                );
                continue;
            }

            if (!sheetSpec.TryGet(entry.TableName, out var tableDef))
            {
                Debug.LogWarning($"Unknown/unsupported sheet '{entry.TableName}'. Ignoring.");
                continue;
            }

            var sheet = FindSheetByName(metadata, entry.TableName);
            if (sheet == null && string.IsNullOrWhiteSpace(entry.Gid))
            {
                throw new InvalidOperationException(
                    $"Sheet '{entry.TableName}' listed in __Index does not exist in the spreadsheet."
                );
            }

            var csv = string.Empty;
            if (!string.IsNullOrWhiteSpace(entry.Gid))
            {
                var gidCsvUrl = BuildCsvExportByGidUrl(spreadsheetId, entry.Gid);
                csv = DownloadText(gidCsvUrl) ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(csv))
            {
                if (!(sheet is Dictionary<string, object> sheetDict))
                {
                    throw new InvalidOperationException(
                        $"Could not resolve sheet metadata for '{entry.TableName}'."
                    );
                }

                if (
                    !sheetDict.TryGetValue("properties", out var propsObj)
                    || propsObj is not Dictionary<string, object> props
                    || !props.TryGetValue("title", out var titleObj)
                )
                {
                    throw new InvalidOperationException(
                        $"Could not read sheet properties for '{entry.TableName}'."
                    );
                }

                var sheetName = titleObj?.ToString();
                if (string.IsNullOrWhiteSpace(sheetName))
                {
                    throw new InvalidOperationException(
                        $"Empty sheet name in metadata for '{entry.TableName}'."
                    );
                }

                var range = $"'{sheetName}'!A:Z";
                var values = GetSheetValues(spreadsheetId, range, apiKey);
                csv = ValuesToCsv(values);
            }

            AddRawSheetSnapshot(
                rawSheetSnapshots,
                entry.TableName,
                entry.Gid,
                entry.Order,
                false,
                csv
            );
            PersistRawSheetExports(spreadsheetId, rawSheetSnapshots);

            var rows = ParseCsv(csv ?? string.Empty);
            if (rows.Count < 1)
            {
                throw new InvalidOperationException(
                    $"Sheet '{entry.TableName}' is missing a header row."
                );
            }

            var headerRow = rows[0];
            ValidateTableHeaders(entry.TableName, headerRow, tableDef, sheetSpec.AllowExtraColumns);
            var parsedRows = ParseTableRows(rows, tableDef);

            tables[entry.TableName] = new ParsedTable
            {
                Definition = tableDef,
                Header = new List<string>(headerRow),
                Rows = parsedRows,
            };
        }

        return tables;
    }

    // ----------------------------
    // Sheets API v4 helpers
    // ----------------------------

    private sealed class SheetMetadata
    {
        public List<object> sheets;
    }

    private static SheetMetadata GetSpreadsheetMetadata(string spreadsheetId, string apiKey)
    {
        var url = BuildSheetMetadataUrl(spreadsheetId, apiKey);
        var json = DownloadJson(url);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                "Could not fetch spreadsheet metadata. Check your API key and spreadsheet ID."
            );
        }

        // Parse minimal JSON to extract sheets
        return ParseMetadataJson(json);
    }

    private static List<List<object>> GetSheetValues(
        string spreadsheetId,
        string range,
        string apiKey
    )
    {
        var url = BuildValuesUrl(spreadsheetId, range, apiKey);
        var json = DownloadJson(url);
        if (string.IsNullOrWhiteSpace(json))
            return new List<List<object>>();

        return ParseValuesJson(json);
    }

    private static object FindSheetByName(SheetMetadata metadata, string sheetName)
    {
        if (metadata?.sheets == null)
            return null;

        foreach (var sheet in metadata.sheets)
        {
            if (sheet is not Dictionary<string, object> sheetDict)
                continue;

            if (!sheetDict.TryGetValue("properties", out var propsObj))
                continue;

            if (propsObj is not Dictionary<string, object> props)
                continue;

            if (!props.TryGetValue("title", out var titleObj))
                continue;

            var title = titleObj?.ToString();
            if (string.Equals(title, sheetName, StringComparison.OrdinalIgnoreCase))
                return sheetDict;
        }

        return null;
    }

    private static void ValidateNoDuplicateSheetNames(SheetMetadata metadata)
    {
        if (metadata?.sheets == null || metadata.sheets.Count == 0)
            return;

        var names = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var sheet in metadata.sheets)
        {
            if (!(sheet is Dictionary<string, object> sheetDict))
                continue;

            if (!sheetDict.TryGetValue("properties", out var propsObj))
                continue;

            if (!(propsObj is Dictionary<string, object> props))
                continue;

            if (!props.TryGetValue("title", out var titleObj))
                continue;

            var title = titleObj?.ToString();
            if (string.IsNullOrEmpty(title))
                continue;

            if (names.TryGetValue(title, out _))
            {
                throw new InvalidOperationException(
                    $"Duplicate sheet name found: '{title}'. All sheet names must be unique."
                );
            }

            names[title] = 1;
        }
    }

    private static SheetMetadata ParseMetadataJson(string json)
    {
        // Minimal JSON parser for sheets metadata
        var sheets = new List<object>();

        // Find "sheets" array
        var sheetsStart = json.IndexOf("\"sheets\":", StringComparison.OrdinalIgnoreCase);
        if (sheetsStart < 0)
            return new SheetMetadata { sheets = sheets };

        var arrStart = json.IndexOf('[', sheetsStart);
        if (arrStart < 0)
            return new SheetMetadata { sheets = sheets };

        var depth = 0;
        var arrEnd = -1;
        for (int i = arrStart; i < json.Length; i++)
        {
            if (json[i] == '[')
                depth++;
            else if (json[i] == ']')
                depth--;

            if (depth == 0)
            {
                arrEnd = i;
                break;
            }
        }

        if (arrEnd < 0)
            return new SheetMetadata { sheets = sheets };

        var sheetsJson = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
        var sheetObjects = SplitTopLevelJsonObjects(sheetsJson);

        foreach (var sheetJson in sheetObjects)
        {
            var sheet = ParseJsonObject(sheetJson);
            if (sheet != null)
                sheets.Add(sheet);
        }

        return new SheetMetadata { sheets = sheets };
    }

    private static List<List<object>> ParseValuesJson(string json)
    {
        var values = new List<List<object>>();

        // Find "values" array
        var valuesStart = json.IndexOf("\"values\":", StringComparison.OrdinalIgnoreCase);
        if (valuesStart < 0)
            return values;

        var arrStart = json.IndexOf('[', valuesStart);
        if (arrStart < 0)
            return values;

        var depth = 0;
        var arrEnd = -1;
        for (int i = arrStart; i < json.Length; i++)
        {
            if (json[i] == '[')
                depth++;
            else if (json[i] == ']')
                depth--;

            if (depth == 0)
            {
                arrEnd = i;
                break;
            }
        }

        if (arrEnd < 0)
            return values;

        var valuesJson = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
        var rowObjects = SplitTopLevelJsonArrays(valuesJson);

        foreach (var rowJson in rowObjects)
        {
            var row = ParseJsonArray(rowJson);
            if (row != null)
                values.Add(row);
        }

        return values;
    }

    private static string ValuesToCsv(List<List<object>> values)
    {
        if (values == null || values.Count == 0)
            return "";

        var sb = new StringBuilder();
        for (int i = 0; i < values.Count; i++)
        {
            var row = values[i];
            for (int j = 0; j < row.Count; j++)
            {
                if (j > 0)
                    sb.Append(",");

                var cell = row[j]?.ToString() ?? "";
                if (cell.Contains(",") || cell.Contains("\"") || cell.Contains("\n"))
                {
                    sb.Append("\"").Append(cell.Replace("\"", "\"\"")).Append("\"");
                }
                else
                {
                    sb.Append(cell);
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static List<string> SplitTopLevelJsonObjects(string json)
    {
        var objects = new List<string>();
        var depth = 0;
        var start = 0;
        var inStr = false;
        var escape = false;

        for (int i = 0; i < json.Length; i++)
        {
            if (escape)
            {
                escape = false;
                continue;
            }

            if (inStr)
            {
                if (json[i] == '\\')
                    escape = true;
                else if (json[i] == '"')
                    inStr = false;
                continue;
            }

            if (json[i] == '"')
            {
                inStr = true;
                continue;
            }

            if (json[i] == '{')
            {
                if (depth == 0)
                    start = i;
                depth++;
            }
            else if (json[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    objects.Add(json.Substring(start, i - start + 1));
                }
            }
        }

        return objects;
    }

    private static List<string> SplitTopLevelJsonArrays(string json)
    {
        var arrays = new List<string>();
        var depth = 0;
        var start = 0;
        var inStr = false;
        var escape = false;

        for (int i = 0; i < json.Length; i++)
        {
            if (escape)
            {
                escape = false;
                continue;
            }

            if (inStr)
            {
                if (json[i] == '\\')
                    escape = true;
                else if (json[i] == '"')
                    inStr = false;
                continue;
            }

            if (json[i] == '"')
            {
                inStr = true;
                continue;
            }

            if (json[i] == '[')
            {
                if (depth == 0)
                    start = i;
                depth++;
            }
            else if (json[i] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    arrays.Add(json.Substring(start, i - start + 1));
                }
            }
        }

        return arrays;
    }

    private static Dictionary<string, object> ParseJsonObject(string json)
    {
        json = (json ?? "").Trim();
        if (!json.StartsWith("{") || !json.EndsWith("}"))
            return null;

        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var inner = json.Substring(1, json.Length - 2).Trim();
        if (string.IsNullOrEmpty(inner))
            return result;

        var pairs = SplitJsonObject(inner);
        foreach (var kv in pairs)
        {
            result[kv.Key] = ParseJsonLikeValue(kv.Value);
        }

        return result;
    }

    private static List<object> ParseJsonArray(string json)
    {
        json = (json ?? "").Trim();
        if (!json.StartsWith("[") || !json.EndsWith("]"))
            return null;

        var result = new List<object>();
        var inner = json.Substring(1, json.Length - 2).Trim();
        if (string.IsNullOrEmpty(inner))
            return result;

        var items = SplitJsonArray(inner);
        foreach (var item in items)
        {
            result.Add(ParseJsonLikeValue(item));
        }

        return result;
    }

    private sealed class IndexEntry
    {
        public string TableName;
        public int Order;
        public string Gid;
    }

    private static List<IndexEntry> ParseIndex(string indexCsv)
    {
        var rows = ParseCsv(indexCsv);
        if (rows.Count < 2)
            throw new InvalidOperationException(
                "__Index must have a header row and at least one data row."
            );

        var header = rows[0].Select(h => (h ?? "").Trim()).ToList();

        var sheetCol = FindColumnIndex(header, "tableName");
        if (sheetCol < 0)
            sheetCol = FindColumnIndex(header, "sheet");
        var enabledCol = FindColumnIndex(header, "enabled");
        var orderCol = FindColumnIndex(header, "order");
        var gidCol = FindColumnIndex(header, "gid");

        if (sheetCol < 0)
            throw new InvalidOperationException(
                "__Index must include a 'tableName' column (or legacy 'sheet')."
            );
        if (enabledCol < 0)
            throw new InvalidOperationException("__Index must include an 'enabled' column.");
        if (orderCol < 0)
            throw new InvalidOperationException("__Index must include an 'order' column.");

        var entries = new List<IndexEntry>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (IsCommentRow(row))
                continue;

            var rawName = GetCell(row, sheetCol).Trim();
            if (string.IsNullOrWhiteSpace(rawName))
                continue;

            // Commented-out entry names:
            // - __Foo
            // - // Foo
            if (
                rawName.StartsWith("_", StringComparison.Ordinal)
                || rawName.StartsWith("__", StringComparison.Ordinal)
                || rawName.StartsWith("//", StringComparison.Ordinal)
            )
                continue;

            if (!IsEnabled(GetCell(row, enabledCol)))
                continue;

            var gid = gidCol >= 0 ? GetCell(row, gidCol).Trim() : string.Empty;

            var ord = 0;
            int.TryParse(
                GetCell(row, orderCol),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ord
            );

            if (!seenNames.Add(rawName))
            {
                throw new InvalidOperationException(
                    $"__Index contains duplicate enabled table '{rawName}'."
                );
            }

            entries.Add(
                new IndexEntry
                {
                    TableName = rawName,
                    Order = ord,
                    Gid = gid,
                }
            );
        }

        // Stable ordering: order asc, then name asc.
        return entries
            .OrderBy(e => e.Order)
            .ThenBy(e => e.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ValidateRequiredTablesInIndex(
        IReadOnlyList<IndexEntry> index,
        SheetSpecDefinition sheetSpec
    )
    {
        if (sheetSpec == null)
            return;

        var enabledIndexNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (index != null)
        {
            for (int i = 0; i < index.Count; i++)
            {
                var name = (index[i]?.TableName ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(name))
                    enabledIndexNames.Add(name);
            }
        }

        var missingRequired = sheetSpec
            .RequiredNonVirtualTableNames.Where(name => !enabledIndexNames.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingRequired.Count == 0)
            return;

        throw new InvalidOperationException(
            "Required sheet tab(s) are missing or disabled in __Index: "
                + string.Join(", ", missingRequired)
                + ". Add each required tab to __Index with enabled=true. "
                + "Header-only tabs are valid and will import as empty arrays/objects."
        );
    }

    private static SheetSpecDefinition LoadSheetSpecDefinition()
    {
        var candidatePaths = new[]
        {
            SheetSpecPath,
            "Assets/data/spark_plug_sheet_spec.json",
            "Assets/Data/sheetSpec.json",
            "Assets/data/sheetSpec.json",
        };

        string resolvedPath = null;
        for (int i = 0; i < candidatePaths.Length; i++)
        {
            if (File.Exists(candidatePaths[i]))
            {
                resolvedPath = candidatePaths[i];
                break;
            }
        }

        if (string.IsNullOrEmpty(resolvedPath))
        {
            throw new InvalidOperationException(
                "Could not locate sheet spec JSON. Expected one of: "
                    + string.Join(", ", candidatePaths)
            );
        }

        var json = File.ReadAllText(resolvedPath, Encoding.UTF8).TrimStart('\uFEFF');
        var parsed = ParseJsonLikeValue(json);
        if (parsed is not Dictionary<string, object> root)
        {
            throw new InvalidOperationException(
                $"Sheet spec file '{resolvedPath}' is invalid. Expected a top-level JSON object."
            );
        }

        Dictionary<string, object> sheetSpecObject = null;
        if (
            root.TryGetValue("sheetSpec", out var nested) && nested is Dictionary<string, object> ns
        )
            sheetSpecObject = ns;
        else if (root.TryGetValue("tables", out _))
            sheetSpecObject = root;

        if (sheetSpecObject == null)
        {
            throw new InvalidOperationException(
                $"Sheet spec file '{resolvedPath}' does not contain a 'sheetSpec' object."
            );
        }

        if (
            !sheetSpecObject.TryGetValue("tables", out var tablesObj)
            || tablesObj is not List<object> tables
        )
        {
            throw new InvalidOperationException(
                $"Sheet spec file '{resolvedPath}' is missing sheetSpec.tables[]."
            );
        }

        var definition = new SheetSpecDefinition();
        if (
            sheetSpecObject.TryGetValue("settings", out var settingsObj)
            && settingsObj is Dictionary<string, object> settings
        )
        {
            var allowExtraColumns = ReadNullableBool(settings, "allowExtraColumns");
            if (allowExtraColumns.HasValue)
                definition.AllowExtraColumns = allowExtraColumns.Value;

            if (
                settings.TryGetValue("reservedSheets", out var reservedObj)
                && reservedObj is List<object> reservedSheets
            )
            {
                for (int i = 0; i < reservedSheets.Count; i++)
                {
                    var reservedName = reservedSheets[i]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(reservedName))
                        definition.AddReservedSheet(reservedName);
                }
            }
        }

        for (int i = 0; i < tables.Count; i++)
        {
            if (tables[i] is not Dictionary<string, object> tableObj)
                continue;

            var name = ReadString(tableObj, "name");
            if (string.IsNullOrEmpty(name))
                continue;

            var table = new SheetSpecTable
            {
                Name = name,
                PackKey = ReadString(tableObj, "packKey") ?? ReadString(tableObj, "outputKey"),
                Emit = ReadString(tableObj, "emit"),
                RowMode = ReadString(tableObj, "rowMode"),
                Required = ReadBool(tableObj, "required"),
                PrimaryKey = ReadString(tableObj, "primaryKey"),
                IsVirtual = ReadBool(tableObj, "virtual") || ReadBool(tableObj, "isVirtual"),
            };

            if (tableObj.TryGetValue("columns", out var colsObj) && colsObj is List<object> columns)
            {
                for (int c = 0; c < columns.Count; c++)
                {
                    if (columns[c] is not Dictionary<string, object> colObj)
                        continue;

                    var colName = ReadString(colObj, "name");
                    if (string.IsNullOrWhiteSpace(colName))
                        continue;

                    var column = new SheetSpecColumn
                    {
                        Name = colName,
                        Required = ReadBool(colObj, "required"),
                        Type = ReadString(colObj, "type"),
                    };

                    table.Columns.Add(column);
                    table.ColumnsByName[colName] = column;
                }
            }

            if (
                tableObj.TryGetValue("columnRenames", out var renamesObj)
                && renamesObj is List<object> renames
            )
            {
                for (int r = 0; r < renames.Count; r++)
                {
                    if (renames[r] is not Dictionary<string, object> renameObj)
                        continue;

                    var from = ReadString(renameObj, "from");
                    var to = ReadString(renameObj, "to");
                    if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                        continue;

                    table.ColumnRenames.Add(new SheetSpecRename { From = from, To = to });
                    table.RenamedColumnsByFrom[from] = to;
                }
            }

            if (
                tableObj.TryGetValue("embed", out var embedObj)
                && embedObj is Dictionary<string, object> embedDict
            )
            {
                table.Embed = new SheetSpecEmbed
                {
                    ParentTable = ReadString(embedDict, "parentTable"),
                    ParentIdColumn = ReadString(embedDict, "parentIdColumn"),
                    ForeignKeyColumn = ReadString(embedDict, "foreignKeyColumn"),
                    As = ReadString(embedDict, "as"),
                };
            }

            definition.Add(table);
        }

        return definition;
    }

    private static int FindColumnIndex(List<string> header, string columnName)
    {
        for (int i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i], columnName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static bool IsEnabled(string raw)
    {
        raw = (raw ?? "").Trim();

        if (string.IsNullOrEmpty(raw))
            throw new InvalidOperationException(
                "'enabled' column requires explicit 'true' or 'false' value (case-insensitive). Empty values are not allowed."
            );

        if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            return false;

        throw new InvalidOperationException(
            $"Invalid 'enabled' value: '{raw}'. Must be 'true' or 'false' (case-insensitive)."
        );
    }

    private static string ReadString(Dictionary<string, object> obj, string key)
    {
        if (obj == null || string.IsNullOrWhiteSpace(key))
            return null;

        if (!obj.TryGetValue(key, out var value) || value == null)
            return null;

        var s = value.ToString().Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static bool ReadBool(Dictionary<string, object> obj, string key)
    {
        if (obj == null || string.IsNullOrWhiteSpace(key))
            return false;

        if (!obj.TryGetValue(key, out var value) || value == null)
            return false;

        if (value is bool b)
            return b;

        var s = value.ToString().Trim();
        if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase))
            return false;

        return false;
    }

    private static bool? ReadNullableBool(Dictionary<string, object> obj, string key)
    {
        if (obj == null || string.IsNullOrWhiteSpace(key))
            return null;

        if (!obj.TryGetValue(key, out var value) || value == null)
            return null;

        if (value is bool b)
            return b;

        var s = value.ToString().Trim();
        if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase))
            return false;

        return null;
    }

    private static void AddRawSheetSnapshot(
        List<RawSheetSnapshot> snapshots,
        string sheetName,
        string gid,
        int order,
        bool isIndex,
        string csv
    )
    {
        if (snapshots == null || string.IsNullOrWhiteSpace(sheetName))
            return;

        snapshots.Add(
            new RawSheetSnapshot
            {
                Name = sheetName.Trim(),
                Gid = (gid ?? string.Empty).Trim(),
                Order = order,
                IsIndex = isIndex,
                Csv = csv ?? string.Empty,
            }
        );
    }

    private static void WriteAiSheetBundle(string spreadsheetId, List<RawSheetSnapshot> snapshots)
    {
        var dir = Path.GetDirectoryName(CsvAiBundlePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var ordered = snapshots ?? new List<RawSheetSnapshot>();
        var sheets = ordered
            .OrderBy(s => s.IsIndex ? 0 : 1)
            .ThenBy(s => s.Order)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s =>
                (object)
                    new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = s.Name,
                        ["gid"] = string.IsNullOrWhiteSpace(s.Gid) ? null : s.Gid,
                        ["order"] = s.Order,
                        ["isIndex"] = s.IsIndex,
                        ["csv"] = s.Csv ?? string.Empty,
                    }
            )
            .ToList();

        var bundle = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "sparkplug-google-sheet-export-v1",
            ["spreadsheetId"] = spreadsheetId ?? string.Empty,
            ["exportedAtUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["sheets"] = sheets,
        };

        File.WriteAllText(CsvAiBundlePath, ToJson(bundle), new UTF8Encoding(false));
    }

    private static void PersistRawSheetExports(
        string spreadsheetId,
        List<RawSheetSnapshot> snapshots
    )
    {
        WriteAiSheetBundle(spreadsheetId, snapshots);
        WriteDataText(snapshots);
    }

    private static void WriteDataText(List<RawSheetSnapshot> snapshots)
    {
        var ordered = (snapshots ?? new List<RawSheetSnapshot>())
            .OrderBy(s => s.IsIndex ? 0 : 1)
            .ThenBy(s => s.Order)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        for (int i = 0; i < ordered.Count; i++)
        {
            var sheet = ordered[i];
            sb.Append(sheet.Name ?? string.Empty).Append('\n').Append('\n');
            sb.Append(sheet.Csv ?? string.Empty);
            if (sb.Length > 0 && sb[sb.Length - 1] != '\n')
                sb.Append('\n');

            sb.Append('\n').Append("---").Append('\n').Append('\n');
        }

        File.WriteAllText(DataTextPath, sb.ToString(), new UTF8Encoding(false));
    }

    private static string GetCell(List<string> row, int col)
    {
        if (col < 0 || col >= row.Count)
            return "";
        return row[col] ?? "";
    }

    private static void ValidateTableHeaders(
        string tableName,
        List<string> headerRow,
        SheetSpecTable tableDef,
        bool allowExtraColumns
    )
    {
        var normalizedHeaders = new HashSet<string>(
            headerRow
                .Select(h => (h ?? string.Empty).Trim())
                .Where(h => !string.IsNullOrWhiteSpace(h)),
            StringComparer.OrdinalIgnoreCase
        );

        for (int i = 0; i < tableDef.Columns.Count; i++)
        {
            var col = tableDef.Columns[i];
            if (!col.Required)
                continue;

            if (normalizedHeaders.Contains(col.Name))
                continue;

            var hasAliasHeader = tableDef.ColumnRenames.Any(rename =>
                string.Equals(rename.From, col.Name, StringComparison.OrdinalIgnoreCase)
                && normalizedHeaders.Contains(rename.To)
            );

            if (!hasAliasHeader)
            {
                throw new InvalidOperationException(
                    $"Sheet '{tableName}' missing required column '{col.Name}'."
                );
            }
        }

        if (allowExtraColumns)
            return;

        for (int i = 0; i < headerRow.Count; i++)
        {
            var header = (headerRow[i] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(header))
                continue;

            if (!tableDef.ColumnsByName.ContainsKey(header))
            {
                throw new InvalidOperationException(
                    $"Sheet '{tableName}' contains unsupported column '{header}'."
                );
            }
        }
    }

    private static List<Dictionary<string, object>> ParseTableRows(
        List<List<string>> rows,
        SheetSpecTable tableDef
    )
    {
        var parsedRows = new List<Dictionary<string, object>>();
        if (rows == null || rows.Count == 0)
            return parsedRows;

        var header = rows[0];
        for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (IsCommentRow(row))
                continue;

            var parsedRow = ParseDataRow(row, header, tableDef);
            if (parsedRow.Count > 0)
                parsedRows.Add(parsedRow);
        }

        return parsedRows;
    }

    private static Dictionary<string, object> ParseDataRow(
        List<string> row,
        List<string> header,
        SheetSpecTable tableDef
    )
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < header.Count; i++)
        {
            var sourceColumn = (header[i] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sourceColumn))
                continue;

            if (
                !TryResolveColumnForSourceHeader(
                    tableDef,
                    sourceColumn,
                    out var canonicalColumn,
                    out var colDef
                )
            )
            {
                continue;
            }

            var rawValue = GetCell(row, i);
            if (string.IsNullOrWhiteSpace(rawValue) && (colDef == null || !colDef.Required))
                continue;

            var parsedValue = ParseValue(rawValue, canonicalColumn, colDef?.Type);
            SetByPathRaw(dict, canonicalColumn, parsedValue);
        }

        return dict;
    }

    private static bool TryResolveColumnForSourceHeader(
        SheetSpecTable tableDef,
        string sourceColumn,
        out string canonicalColumn,
        out SheetSpecColumn colDef
    )
    {
        canonicalColumn = null;
        colDef = null;
        if (tableDef == null || string.IsNullOrWhiteSpace(sourceColumn))
            return false;

        if (tableDef.ColumnsByName.TryGetValue(sourceColumn, out colDef))
        {
            canonicalColumn = tableDef.RenamedColumnsByFrom.TryGetValue(
                sourceColumn,
                out var renamed
            )
                ? renamed
                : sourceColumn;
            return true;
        }

        for (int i = 0; i < tableDef.ColumnRenames.Count; i++)
        {
            var rename = tableDef.ColumnRenames[i];
            if (!string.Equals(rename.To, sourceColumn, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!tableDef.ColumnsByName.TryGetValue(rename.From, out colDef))
                return false;

            canonicalColumn = rename.To;
            return true;
        }

        return false;
    }

    // ----------------------------
    // Download + CSV parsing
    // ----------------------------

    private static string DownloadText(string url)
    {
        using var req = UnityWebRequest.Get(url);
        req.SetRequestHeader("User-Agent", "Unity-Editor/SparkPlug-Importer");

        req.SendWebRequest();
        while (!req.isDone)
        {
            // Keep editor responsive-ish.
            EditorApplication.QueuePlayerLoopUpdate();
            Thread.Sleep(8);
        }

        if (req.result != UnityWebRequest.Result.Success)
        {
            // For sheet-by-name requests, Google can return 400 for "not found" or not-shared.
            // Treat missing sheets as null so the importer can continue.
            if (req.responseCode == 404)
                return null;

            // Often indicates permission or wrong ID / wrong published mode.
            var hint =
                req.responseCode == 400
                    ? "400 often means: (1) The sheet isn't shared 'Anyone with the link can view', or (2) you used a normal ID but the sheet is publish-only (or vice versa)."
                    : null;

            throw new InvalidOperationException(
                $"Download failed: {req.error} (HTTP {req.responseCode}).{(hint == null ? "" : " " + hint)}\nURL: {url}"
            );
        }

        return req.downloadHandler?.text;
    }

    private static string DownloadJson(string url)
    {
        using var req = UnityWebRequest.Get(url);
        req.SetRequestHeader("User-Agent", "Unity-Editor/SparkPlug-Importer");

        req.SendWebRequest();
        while (!req.isDone)
        {
            EditorApplication.QueuePlayerLoopUpdate();
            Thread.Sleep(8);
        }

        if (req.result != UnityWebRequest.Result.Success)
        {
            if (req.responseCode == 404)
                return null;

            var hint =
                req.responseCode == 400 ? "Invalid request. Check your API key and spreadsheet ID."
                : req.responseCode == 403
                    ? "Access denied. Check your API key has Sheets API v4 enabled."
                : null;

            throw new InvalidOperationException(
                $"Sheets API request failed: {req.error} (HTTP {req.responseCode}).{(hint == null ? "" : " " + hint)}\nURL: {url}"
            );
        }

        return req.downloadHandler?.text;
    }

    private static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var current = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < csv.Length; i++)
        {
            var c = csv[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    current.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                        i++;

                    current.Add(sb.ToString().Trim());
                    sb.Clear();

                    if (current.Any(s => !string.IsNullOrEmpty(s)))
                        rows.Add(new List<string>(current));

                    current.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        if (sb.Length > 0 || current.Count > 0)
        {
            current.Add(sb.ToString().Trim());
            if (current.Any(s => !string.IsNullOrEmpty(s)))
                rows.Add(current);
        }

        return rows;
    }

    private static bool IsCommentRow(List<string> row)
    {
        if (row == null || row.Count == 0)
            return true;

        var firstCell = (row[0] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(firstCell))
            return true;

        return firstCell.StartsWith("_", StringComparison.Ordinal)
            || firstCell.StartsWith("//", StringComparison.Ordinal);
    }

    private static void SetByPath(Dictionary<string, object> dict, string path, object value)
    {
        if (string.IsNullOrEmpty(path))
            return;

        var parsed =
            (value is Dictionary<string, object> || value is List<object>)
                ? value
                : ParseValue(value?.ToString(), path);

        var parts = path.Split('.');
        if (parts.Length == 1)
        {
            dict[parts[0]] = parsed;
            return;
        }

        var current = dict;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];

            if (!current.TryGetValue(part, out var next))
            {
                var nextDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                current[part] = nextDict;
                current = nextDict;
                continue;
            }

            if (next is Dictionary<string, object> asDict)
            {
                current = asDict;
                continue;
            }

            throw new InvalidOperationException(
                $"Key-path conflict while setting '{path}'. '{part}' already exists but is not an object."
            );
        }

        current[parts[^1]] = parsed;
    }

    private static void SetByPathRaw(Dictionary<string, object> dict, string path, object value)
    {
        if (string.IsNullOrEmpty(path))
            return;

        var parts = path.Split('.');
        if (parts.Length == 1)
        {
            dict[parts[0]] = value;
            return;
        }

        var current = dict;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (!current.TryGetValue(part, out var next))
            {
                var nextDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                current[part] = nextDict;
                current = nextDict;
                continue;
            }

            if (next is Dictionary<string, object> asDict)
            {
                current = asDict;
                continue;
            }

            throw new InvalidOperationException(
                $"Key-path conflict while setting '{path}'. '{part}' already exists but is not an object."
            );
        }

        current[parts[^1]] = value;
    }

    private static object ParseValue(string s, string path, string typeHint = null)
    {
        if (s == null)
            return null;

        s = s.Trim();
        var normalizedType = (typeHint ?? string.Empty).Trim();
        var isJsonColumn =
            path.EndsWith("_json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedType, "json", StringComparison.OrdinalIgnoreCase);

        if (isJsonColumn)
        {
            if (string.IsNullOrEmpty(s))
                return null;

            // Strict mode: fail loud if JSON is invalid.
            // Accept either raw JSON (preferred) OR a quoted/escaped JSON string (common when copy/pasting).
            var raw = s;

            // If the entire cell is a quoted string, unquote and unescape CSV-style doubled quotes.
            // Example:
            //   "[{""type"":""resourceAtLeast""}]"  ->  [{"type":"resourceAtLeast"}]
            if (
                raw.Length >= 2
                && raw.StartsWith("\"", StringComparison.Ordinal)
                && raw.EndsWith("\"", StringComparison.Ordinal)
            )
            {
                raw = raw.Substring(1, raw.Length - 2);

                // Undo CSV-style escaping ("" -> ")
                raw = raw.Replace("\"\"", "\"");

                // Also undo common backslash escaping for quotes if present.
                raw = raw.Replace("\\\"", "\"");
            }

            raw = raw.Trim();
            if (
                raw.StartsWith("[", StringComparison.Ordinal)
                || raw.StartsWith("{", StringComparison.Ordinal)
            )
            {
                var parsedJson = ParseJsonLikeValue(raw);
                if (parsedJson is Dictionary<string, object> || parsedJson is List<object>)
                    return parsedJson;

                throw new InvalidOperationException(
                    $"Column '{path}' expects a JSON object/array value. Got: {s}"
                );
            }

            raw = RemoveWhitespaceOutsideJsonStrings(raw);
            var parsedFallback = ParseJsonLikeValue(raw);
            if (parsedFallback is Dictionary<string, object> || parsedFallback is List<object>)
                return parsedFallback;

            throw new InvalidOperationException(
                $"Column '{path}' expects a JSON object/array value. Got: {s}"
            );
        }

        if (string.IsNullOrEmpty(s))
            return null;

        if (string.Equals(normalizedType, "stringList", StringComparison.OrdinalIgnoreCase))
            return ParseCommaList(s);

        if (IsStringLikeTypeHint(normalizedType))
            return s;

        if (string.Equals(normalizedType, "bool", StringComparison.OrdinalIgnoreCase))
            return bool.TryParse(s, out var boolValue) ? boolValue : s;

        if (string.Equals(normalizedType, "int", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(
                s,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var intValue
            )
                ? intValue
                : s;
        }

        if (
            string.Equals(normalizedType, "double", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedType, "number", StringComparison.OrdinalIgnoreCase)
        )
        {
            return double.TryParse(
                s,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var dblValue
            )
                ? dblValue
                : s;
        }

        if (bool.TryParse(s, out var b))
            return b;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return i;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;
        if (string.Equals(s, "null", StringComparison.OrdinalIgnoreCase))
            return null;

        return s;
    }

    private static bool IsStringLikeTypeHint(string normalizedType)
    {
        if (string.IsNullOrWhiteSpace(normalizedType))
            return false;

        if (string.Equals(normalizedType, "string", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(normalizedType, "enum", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(normalizedType, "path", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalizedType.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string RemoveWhitespaceOutsideJsonStrings(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        // Removes spaces/newlines/tabs outside of JSON strings.
        // Keeps whitespace inside quoted strings intact.
        var sb = new StringBuilder(s.Length);
        var inStr = false;
        var escape = false;

        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];

            if (escape)
            {
                sb.Append(c);
                escape = false;
                continue;
            }

            if (inStr)
            {
                sb.Append(c);
                if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inStr = false;
                }
                continue;
            }

            if (c == '"')
            {
                inStr = true;
                sb.Append(c);
                continue;
            }

            // Outside strings: drop all whitespace (space, tab, CR, LF, etc.).
            if (char.IsWhiteSpace(c))
                continue;

            sb.Append(c);
        }

        return sb.ToString();
    }

    // Minimal JSON-like parser suitable for cells containing simple arrays/objects.
    // It intentionally supports:
    // - [] / {}
    // - nested objects/arrays
    // - quoted strings
    // - numbers/bools
    //
    // If it can't parse, it throws (fail loud).
    private static object ParseJsonLikeValue(string s)
    {
        s = (s ?? "").Trim();
        if (string.IsNullOrEmpty(s))
            return new List<object>();

        if (s == "[]")
            return new List<object>();
        if (s == "{}")
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (s.StartsWith("[", StringComparison.Ordinal))
        {
            if (!s.EndsWith("]", StringComparison.Ordinal))
                throw new InvalidOperationException($"Invalid _json array: {s}");

            var list = new List<object>();
            var inner = s.Substring(1, s.Length - 2).Trim();
            if (string.IsNullOrEmpty(inner))
                return list;

            var items = SplitJsonArray(inner);
            foreach (var item in items)
                list.Add(ParseJsonLikeValue(item));

            return list;
        }

        if (s.StartsWith("{", StringComparison.Ordinal))
        {
            if (!s.EndsWith("}", StringComparison.Ordinal))
                throw new InvalidOperationException($"Invalid _json object: {s}");

            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var inner = s.Substring(1, s.Length - 2).Trim();
            if (string.IsNullOrEmpty(inner))
                return dict;

            var pairs = SplitJsonObject(inner);
            foreach (var kv in pairs)
                dict[kv.Key] = ParseJsonLikeValue(kv.Value);

            return dict;
        }

        if (
            s.StartsWith("\"", StringComparison.Ordinal)
            && s.EndsWith("\"", StringComparison.Ordinal)
            && s.Length >= 2
        )
            return s.Substring(1, s.Length - 2).Replace("\\\"", "\"");

        if (bool.TryParse(s, out var b))
            return b;

        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;

        return s;
    }

    private static List<string> SplitJsonArray(string inner)
    {
        var items = new List<string>();
        var depth = 0;
        var start = 0;
        var inStr = false;
        var escape = false;

        for (int i = 0; i < inner.Length; i++)
        {
            var c = inner[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (inStr)
            {
                if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inStr = false;
                }
                continue;
            }

            if (c == '"')
            {
                inStr = true;
                continue;
            }

            if (c == '{' || c == '[')
                depth++;
            else if (c == '}' || c == ']')
                depth--;
            else if (c == ',' && depth == 0)
            {
                items.Add(inner.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }

        if (start < inner.Length)
            items.Add(inner.Substring(start).Trim());

        return items;
    }

    private static List<KeyValuePair<string, string>> SplitJsonObject(string inner)
    {
        var result = new List<KeyValuePair<string, string>>();

        var depth = 0;
        var start = 0;
        var inStr = false;
        var escape = false;

        for (int i = 0; i < inner.Length; i++)
        {
            var c = inner[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (inStr)
            {
                if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inStr = false;
                }
                continue;
            }

            if (c == '"')
            {
                inStr = true;
                continue;
            }

            if (c == '{' || c == '[')
                depth++;
            else if (c == '}' || c == ']')
                depth--;
            else if (c == ',' && depth == 0)
            {
                AddJsonPair(inner.Substring(start, i - start).Trim(), result);
                start = i + 1;
            }
        }

        if (start < inner.Length)
            AddJsonPair(inner.Substring(start).Trim(), result);

        return result;
    }

    private static void AddJsonPair(string seg, List<KeyValuePair<string, string>> result)
    {
        var colon = seg.IndexOf(':');
        if (colon <= 0)
            throw new InvalidOperationException($"Invalid _json object pair: {seg}");

        var k = seg.Substring(0, colon).Trim();
        var v = seg.Substring(colon + 1).Trim();

        k = k.Trim().Trim('"');
        result.Add(new KeyValuePair<string, string>(k, v));
    }

    // ----------------------------
    // Pack building (spec-driven)
    // ----------------------------

    private static Dictionary<string, object> BuildPackFromSpec(
        Dictionary<string, ParsedTable> tables,
        SheetSpecDefinition sheetSpec
    )
    {
        var pack = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var tableDef in sheetSpec.TablesInOrder)
        {
            if (tableDef == null || tableDef.IsVirtual || string.IsNullOrWhiteSpace(tableDef.Name))
                continue;

            if (!tables.TryGetValue(tableDef.Name, out var parsed))
            {
                if (tableDef.Required)
                {
                    throw new InvalidOperationException(
                        $"Required table '{tableDef.Name}' was not imported. Ensure it exists and is enabled in __Index."
                    );
                }

                continue;
            }

            var emit = (tableDef.Emit ?? "list").Trim().ToLowerInvariant();
            if (emit == "subtable")
                continue;

            var packKey = ResolvePackKey(tableDef);
            if (string.IsNullOrWhiteSpace(packKey))
                continue;

            if (emit == "object")
            {
                pack[packKey] = EmitObjectRows(parsed.Rows, tableDef);
                continue;
            }

            pack[packKey] = EmitListRows(parsed.Rows);
        }

        foreach (var tableDef in sheetSpec.TablesInOrder)
        {
            if (tableDef == null || tableDef.IsVirtual || string.IsNullOrWhiteSpace(tableDef.Name))
                continue;

            var emit = (tableDef.Emit ?? "list").Trim().ToLowerInvariant();
            if (emit != "subtable")
                continue;

            if (!tables.TryGetValue(tableDef.Name, out var parsed))
            {
                if (tableDef.Required)
                {
                    throw new InvalidOperationException(
                        $"Required table '{tableDef.Name}' was not imported. Ensure it exists and is enabled in __Index."
                    );
                }

                continue;
            }

            if (tableDef.Embed == null)
            {
                var packKey = ResolvePackKey(tableDef);
                if (!string.IsNullOrWhiteSpace(packKey))
                    pack[packKey] = EmitListRows(parsed.Rows);
                continue;
            }

            EmbedSubtableRows(pack, tableDef, parsed.Rows, sheetSpec);
        }

        NormalizeNodePriceCurveTaggedUnion(pack);
        MirrorMetaIdentityValues(pack);

        return pack;
    }

    private static List<object> EmitListRows(List<Dictionary<string, object>> rows)
    {
        if (rows == null || rows.Count == 0)
            return new List<object>();

        return rows.Select(r => (object)CloneDictionary(r)).ToList();
    }

    private static Dictionary<string, object> EmitObjectRows(
        List<Dictionary<string, object>> rows,
        SheetSpecTable tableDef
    )
    {
        var rowMode = (tableDef.RowMode ?? string.Empty).Trim().ToLowerInvariant();
        if (rowMode == "kv")
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (rows == null || rows.Count == 0)
                return result;

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var key = GetValueByPath(row, "key")?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var value = CloneValue(GetValueByPath(row, "value"));
                if (string.Equals(key, "schemaVersion", StringComparison.OrdinalIgnoreCase))
                {
                    if (value != null && value is not string)
                        value = Convert.ToString(value, CultureInfo.InvariantCulture);
                }
                else if (value is string stringValue)
                {
                    value = ParseValue(stringValue, key, null);
                }

                SetByPathRaw(result, key, value);
            }

            return result;
        }

        if (rows == null || rows.Count == 0)
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        return CloneDictionary(rows[0]);
    }

    private static void EmbedSubtableRows(
        Dictionary<string, object> pack,
        SheetSpecTable subtableDef,
        List<Dictionary<string, object>> rows,
        SheetSpecDefinition sheetSpec
    )
    {
        var embed = subtableDef.Embed;
        if (
            embed == null
            || string.IsNullOrWhiteSpace(embed.ParentTable)
            || string.IsNullOrWhiteSpace(embed.ParentIdColumn)
            || string.IsNullOrWhiteSpace(embed.ForeignKeyColumn)
            || string.IsNullOrWhiteSpace(embed.As)
        )
        {
            throw new InvalidOperationException(
                $"Subtable '{subtableDef.Name}' has an invalid embed block."
            );
        }

        var parentPackKey = embed.ParentTable;
        if (sheetSpec.TryGet(embed.ParentTable, out var parentDef))
            parentPackKey = ResolvePackKey(parentDef);

        if (
            !pack.TryGetValue(parentPackKey, out var parentObj)
            || parentObj is not List<object> parentRows
        )
        {
            if (rows == null || rows.Count == 0)
                return;

            throw new InvalidOperationException(
                $"Subtable '{subtableDef.Name}' cannot embed into '{embed.ParentTable}' because pack key '{parentPackKey}' is missing or not a list."
            );
        }

        var grouped = rows.GroupBy(
                r => GetValueByPath(r, embed.ForeignKeyColumn)?.ToString(),
                StringComparer.OrdinalIgnoreCase
            )
            .ToDictionary(
                g => g.Key ?? string.Empty,
                g => g.ToList(),
                StringComparer.OrdinalIgnoreCase
            );

        for (int i = 0; i < parentRows.Count; i++)
        {
            if (parentRows[i] is not Dictionary<string, object> parentRow)
                continue;

            var parentId = GetValueByPath(parentRow, embed.ParentIdColumn)?.ToString();
            if (string.IsNullOrWhiteSpace(parentId))
                continue;

            if (!grouped.TryGetValue(parentId, out var matches) || matches.Count == 0)
                continue;

            var embeddedRows = new List<object>(matches.Count);
            for (int j = 0; j < matches.Count; j++)
            {
                var embedded = CloneDictionary(matches[j]);
                RemoveByPath(embedded, embed.ForeignKeyColumn);
                embeddedRows.Add(embedded);
            }

            SetByPathRaw(parentRow, embed.As, embeddedRows);
        }
    }

    private static void NormalizeNodePriceCurveTaggedUnion(Dictionary<string, object> pack)
    {
        if (
            !pack.TryGetValue("nodes", out var nodesObj)
            || nodesObj is not List<object> nodes
            || nodes.Count == 0
        )
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is not Dictionary<string, object> node)
                continue;

            var type = GetValueByPath(node, "leveling.priceCurve.type")
                ?.ToString()
                ?.Trim()
                ?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(type))
                continue;

            var keepTable = string.Equals(type, "table", StringComparison.OrdinalIgnoreCase);
            var keepSegments =
                string.Equals(type, "segments", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "piecewise", StringComparison.OrdinalIgnoreCase);

            if (!keepTable)
                RemoveByPath(node, "leveling.priceCurve.table");

            if (!keepSegments)
                RemoveByPath(node, "leveling.priceCurve.segments");

            if (keepTable || keepSegments)
            {
                RemoveByPath(node, "leveling.priceCurve.basePrice");
                RemoveByPath(node, "leveling.priceCurve.growth");
                RemoveByPath(node, "leveling.priceCurve.increment");
                continue;
            }

            var nodeId = GetValueByPath(node, "id")?.ToString() ?? $"index {i}";
            var hasBasePrice = GetValueByPath(node, "leveling.priceCurve.basePrice") != null;
            if (string.Equals(type, "exponential", StringComparison.OrdinalIgnoreCase))
            {
                var hasGrowth = GetValueByPath(node, "leveling.priceCurve.growth") != null;
                if (!hasBasePrice || !hasGrowth)
                {
                    Debug.LogWarning(
                        $"Node '{nodeId}' priceCurve.type='exponential' should define leveling.priceCurve.basePrice and leveling.priceCurve.growth."
                    );
                }
            }
            else if (string.Equals(type, "linear", StringComparison.OrdinalIgnoreCase))
            {
                var hasIncrement = GetValueByPath(node, "leveling.priceCurve.increment") != null;
                if (!hasBasePrice || !hasIncrement)
                {
                    Debug.LogWarning(
                        $"Node '{nodeId}' priceCurve.type='linear' should define leveling.priceCurve.basePrice and leveling.priceCurve.increment."
                    );
                }
            }
        }
    }

    private static void MirrorMetaIdentityValues(Dictionary<string, object> pack)
    {
        if (
            !pack.TryGetValue("meta", out var metaObj)
            || metaObj is not Dictionary<string, object> meta
        )
            return;

        if (
            !pack.ContainsKey("gameId")
            && meta.TryGetValue("gameId", out var gameId)
            && gameId != null
        )
            pack["gameId"] = gameId;

        if (
            !pack.ContainsKey("version")
            && meta.TryGetValue("version", out var version)
            && version != null
        )
            pack["version"] = version;
    }

    private static object GetValueByPath(Dictionary<string, object> dict, string path)
    {
        if (dict == null || string.IsNullOrWhiteSpace(path))
            return null;

        var parts = path.Split('.');
        object current = dict;
        for (int i = 0; i < parts.Length; i++)
        {
            if (current is not Dictionary<string, object> curDict)
                return null;

            if (!curDict.TryGetValue(parts[i], out current))
                return null;
        }

        return current;
    }

    private static void RemoveByPath(Dictionary<string, object> dict, string path)
    {
        if (dict == null || string.IsNullOrWhiteSpace(path))
            return;

        var parts = path.Split('.');
        if (parts.Length == 1)
        {
            dict.Remove(parts[0]);
            return;
        }

        var current = dict;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (
                !current.TryGetValue(parts[i], out var next)
                || next is not Dictionary<string, object> nextDict
            )
                return;

            current = nextDict;
        }

        current.Remove(parts[^1]);
    }

    private static Dictionary<string, object> CloneDictionary(Dictionary<string, object> source)
    {
        var clone = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
            return clone;

        foreach (var kv in source)
            clone[kv.Key] = CloneValue(kv.Value);

        return clone;
    }

    private static object CloneValue(object value)
    {
        if (value is Dictionary<string, object> dict)
            return CloneDictionary(dict);

        if (value is List<object> list)
            return list.Select(CloneValue).ToList();

        return value;
    }

    private static object ParseCommaList(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return new List<object>();
        return s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => (object)x.Trim().Trim('"', '\'', '[', ']'))
            .ToList();
    }

    private static string ResolvePackKey(SheetSpecTable table)
    {
        if (table == null)
            return null;

        if (!string.IsNullOrWhiteSpace(table.PackKey))
            return table.PackKey.Trim();

        return ToLowerCamelCase(table.Name);
    }

    private static string ToLowerCamelCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var trimmed = value.Trim();
        if (trimmed.Length == 1)
            return trimmed.ToLowerInvariant();

        return char.ToLowerInvariant(trimmed[0]) + trimmed.Substring(1);
    }

    // ----------------------------
    // Validation + Write
    // ----------------------------

    private static void ValidatePack(Dictionary<string, object> pack)
    {
        if (!pack.ContainsKey("gameId") || pack["gameId"] == null)
            throw new InvalidOperationException("Pack must have gameId.");

        if (
            !pack.TryGetValue("resources", out var resObj)
            || resObj is not List<object> res
            || res.Count == 0
        )
            throw new InvalidOperationException("Pack must have at least one resource.");

        if (
            !pack.TryGetValue("zones", out var zonesObj)
            || zonesObj is not List<object> zones
            || zones.Count == 0
        )
            throw new InvalidOperationException("Pack must have at least one zone.");

        ValidatePackAgainstSchema(pack);
    }

    private static void ValidatePackAgainstSchema(Dictionary<string, object> pack)
    {
        if (!File.Exists(SchemaPath))
        {
            throw new InvalidOperationException(
                $"Schema file not found: {SchemaPath}. Cannot validate import."
            );
        }

        var schemaJson = File.ReadAllText(SchemaPath, Encoding.UTF8).TrimStart('\uFEFF');
        var parsedSchema = ParseJsonLikeValue(schemaJson);
        if (parsedSchema is not Dictionary<string, object> schemaRoot)
        {
            throw new InvalidOperationException(
                $"Schema file '{SchemaPath}' is invalid. Expected a top-level JSON object."
            );
        }

        var errors = new List<string>();
        ValidateNodeAgainstSchema(pack, schemaRoot, "$", errors);

        if (errors.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Pack failed spark_plug_definition_schema validation:");
            for (int i = 0; i < errors.Count; i++)
                sb.AppendLine($"- {errors[i]}");

            if (errors.Count >= MaxSchemaValidationErrors)
                sb.AppendLine($"- ...stopped after {MaxSchemaValidationErrors} errors.");

            throw new InvalidOperationException(sb.ToString().TrimEnd());
        }
    }

    private static void ValidateNodeAgainstSchema(
        object dataNode,
        object schemaNode,
        string path,
        List<string> errors
    )
    {
        if (errors.Count >= MaxSchemaValidationErrors)
            return;

        if (schemaNode is Dictionary<string, object> schemaObj)
        {
            if (dataNode is not Dictionary<string, object> dataObj)
            {
                AddSchemaError(errors, $"{path}: expected object.");
                return;
            }

            // Treat an empty object in the schema as an "open object".
            // This allows schema sections like `args: {}` to accept arbitrary keys
            // (where the key set is determined by the runtime `type` field), without
            // requiring schema updates for every new arg.
            if (schemaObj.Count == 0)
                return;

            foreach (var dataKv in dataObj)
            {
                if (!schemaObj.TryGetValue(dataKv.Key, out var childSchema))
                {
                    AddSchemaError(
                        errors,
                        $"{path}.{dataKv.Key}: key is not defined by spark_plug_definition_schema."
                    );
                    if (errors.Count >= MaxSchemaValidationErrors)
                        return;
                    continue;
                }

                ValidateNodeAgainstSchema(
                    dataKv.Value,
                    childSchema,
                    $"{path}.{dataKv.Key}",
                    errors
                );
                if (errors.Count >= MaxSchemaValidationErrors)
                    return;
            }

            return;
        }

        if (schemaNode is List<object> schemaList)
        {
            if (dataNode is not List<object> dataList)
            {
                AddSchemaError(errors, $"{path}: expected array.");
                return;
            }

            if (schemaList.Count == 0)
                return;

            var itemSchema = schemaList[0];
            for (int i = 0; i < dataList.Count; i++)
            {
                ValidateNodeAgainstSchema(dataList[i], itemSchema, $"{path}[{i}]", errors);
                if (errors.Count >= MaxSchemaValidationErrors)
                    return;
            }

            return;
        }

        ValidatePrimitiveAgainstSchemaToken(dataNode, schemaNode, path, errors);
    }

    private static void ValidatePrimitiveAgainstSchemaToken(
        object dataNode,
        object schemaNode,
        string path,
        List<string> errors
    )
    {
        if (schemaNode == null)
            return; // null in schema is treated as "nullable/unspecified"

        if (schemaNode is bool)
        {
            if (dataNode is not bool)
                AddSchemaError(errors, $"{path}: expected boolean.");
            return;
        }

        if (IsNumber(schemaNode))
        {
            if (!IsNumber(dataNode))
                AddSchemaError(errors, $"{path}: expected number.");
            return;
        }

        if (schemaNode is not string schemaToken)
            return;

        var optional = IsOptionalSchemaToken(schemaToken);
        if (dataNode == null)
        {
            if (!optional)
                AddSchemaError(errors, $"{path}: value is required.");
            return;
        }

        var token = schemaToken.Trim();
        var tokenParts = token
            .Split('|')
            .Select(p => p.Trim().TrimEnd('?'))
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (tokenParts.Count == 0)
            return;

        if (
            tokenParts.Any(p =>
                string.Equals(p, "numberString", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p, "bigNumberString", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            // Accept either a numeric string OR a numeric value already parsed from CSV.
            if (dataNode is string)
                return;

            if (!IsNumber(dataNode))
                AddSchemaError(errors, $"{path}: expected number or numeric string.");
            return;
        }

        if (tokenParts.All(IsConcreteEnumToken))
        {
            if (dataNode is not string enumValue)
            {
                AddSchemaError(errors, $"{path}: expected enum string.");
                return;
            }

            if (!tokenParts.Contains(enumValue, StringComparer.Ordinal))
            {
                AddSchemaError(
                    errors,
                    $"{path}: '{enumValue}' is not allowed. Expected one of [{string.Join(", ", tokenParts)}]."
                );
            }

            return;
        }

        if (LooksNumericLiteralToken(token))
        {
            if (!(dataNode is string) && !IsNumber(dataNode))
                AddSchemaError(errors, $"{path}: expected number-like value.");
            return;
        }

        if (dataNode is not string)
            AddSchemaError(errors, $"{path}: expected string.");
    }

    private static bool LooksNumericLiteralToken(string token) =>
        double.TryParse(
            token.Trim().TrimEnd('?'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out _
        );

    private static bool IsOptionalSchemaToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Split('|');
        return parts.Any(p => p.Trim().EndsWith("?", StringComparison.Ordinal));
    }

    private static bool IsConcreteEnumToken(string tokenPart)
    {
        if (string.IsNullOrWhiteSpace(tokenPart))
            return false;

        var lowered = tokenPart.Trim();
        // Numeric literals should not be treated as enum tokens.
        if (double.TryParse(lowered, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return false;

        if (lowered.IndexOf('.') >= 0 || lowered.IndexOf('[') >= 0 || lowered.IndexOf(']') >= 0)
            return false;

        if (
            lowered.IndexOf("string", StringComparison.OrdinalIgnoreCase) >= 0
            || lowered.IndexOf("number", StringComparison.OrdinalIgnoreCase) >= 0
            || lowered.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0
            || lowered.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
        )
            return false;

        return true;
    }

    private static bool IsNumber(object value)
    {
        return value is sbyte
            || value is byte
            || value is short
            || value is ushort
            || value is int
            || value is uint
            || value is long
            || value is ulong
            || value is float
            || value is double
            || value is decimal;
    }

    private static void AddSchemaError(List<string> errors, string message)
    {
        if (errors.Count < MaxSchemaValidationErrors)
            errors.Add(message);
    }

    private static void WriteJson(
        Dictionary<string, object> pack,
        string outputPath,
        string resourcesFallbackOutputPath
    )
    {
        var json = ToJson(pack);
        WriteTextAsset(outputPath, json);
        if (!string.IsNullOrWhiteSpace(resourcesFallbackOutputPath))
            WriteTextAsset(resourcesFallbackOutputPath, json);
    }

    private static void WriteTextAsset(string assetPath, string content)
    {
        var dir = Path.GetDirectoryName(assetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(assetPath, content ?? string.Empty, Encoding.UTF8);
    }

    private static string NormalizeOutputJsonPath(string assetPath)
    {
        var normalized = NormalizeOptionalAssetPath(assetPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (!normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return null;

        if (
            !normalized.StartsWith(
                DefaultDefinitionsDirectory + "/",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return null;
        }

        return normalized;
    }

    private static string NormalizeOptionalAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return string.Empty;

        return assetPath.Trim().Replace("\\", "/");
    }

    private static string NormalizeOptionalValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static void EnsureGameDefinitionAddressable(
        string assetPath,
        string address,
        string preferredGroupName
    )
    {
        if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(address))
            return;

        try
        {
            var settingsDefaultType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor"
            );
            if (settingsDefaultType == null)
                return;

            var settings = settingsDefaultType
                .GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            if (settings == null)
            {
                var getSettingsMethod = settingsDefaultType.GetMethod(
                    "GetSettings",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(bool) },
                    null
                );
                settings = getSettingsMethod?.Invoke(null, new object[] { true });
            }

            if (settings == null)
            {
                Debug.LogWarning(
                    $"[GoogleSheetImporter] Addressables settings are unavailable. '{assetPath}' was not assigned an address."
                );
                return;
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid))
                return;

            var settingsType = settings.GetType();
            var defaultGroup = settingsType
                .GetProperty("DefaultGroup", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(settings);
            var targetGroup = ResolveTargetAddressableGroup(settings, preferredGroupName) ?? defaultGroup;
            if (targetGroup == null)
            {
                Debug.LogWarning(
                    $"[GoogleSheetImporter] Addressables default group is missing. '{assetPath}' was not assigned an address."
                );
                return;
            }

            object entry = null;
            var createOrMoveMethods = settingsType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, "CreateOrMoveEntry", StringComparison.Ordinal))
                .ToArray();

            for (int i = 0; i < createOrMoveMethods.Length && entry == null; i++)
            {
                var method = createOrMoveMethods[i];
                var parameters = method.GetParameters();
                if (parameters.Length < 2 || parameters[0].ParameterType != typeof(string))
                    continue;
                if (!parameters[1].ParameterType.IsAssignableFrom(targetGroup.GetType()))
                    continue;

                var args = new object[parameters.Length];
                args[0] = guid;
                args[1] = targetGroup;
                for (int p = 2; p < parameters.Length; p++)
                {
                    args[p] = parameters[p].ParameterType == typeof(bool)
                        ? false
                        : parameters[p].HasDefaultValue
                            ? parameters[p].DefaultValue
                            : null;
                }

                entry = method.Invoke(settings, args);
            }

            if (entry == null)
                return;

            var entryType = entry.GetType();
            var addressProperty = entryType.GetProperty(
                "address",
                BindingFlags.Public | BindingFlags.Instance
            );
            var currentAddress = addressProperty?.GetValue(entry)?.ToString();
            if (!string.Equals(currentAddress, address, StringComparison.Ordinal))
                addressProperty?.SetValue(entry, address);

            AssetDatabase.SaveAssets();
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                $"[GoogleSheetImporter] Failed to assign addressable '{address}' to '{assetPath}': {ex.Message}"
            );
        }
    }

    private static object ResolveTargetAddressableGroup(object settings, string preferredGroupName)
    {
        if (settings == null || string.IsNullOrWhiteSpace(preferredGroupName))
            return null;

        var settingsType = settings.GetType();
        var groupsObj = settingsType
            .GetProperty("groups", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(settings) as System.Collections.IEnumerable;
        if (groupsObj == null)
            return null;

        foreach (var group in groupsObj)
        {
            if (group == null)
                continue;

            var groupName = group
                .GetType()
                .GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(group)
                ?.ToString();
            if (string.Equals(groupName, preferredGroupName, StringComparison.OrdinalIgnoreCase))
                return group;
        }

        return null;
    }

    private static void BuildAddressablesPlayerContent()
    {
        try
        {
            var settingsType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, Unity.Addressables.Editor"
            );
            if (settingsType == null)
                return;

            var resultType = Type.GetType(
                "UnityEditor.AddressableAssets.Build.AddressablesPlayerBuildResult, Unity.Addressables.Editor"
            );
            if (resultType == null)
                return;

            var buildMethod = settingsType.GetMethod(
                "BuildPlayerContent",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { resultType.MakeByRefType() },
                null
            );
            if (buildMethod == null)
                return;

            EditorUtility.DisplayProgressBar(
                "Google Sheet Import",
                "Rebuilding Addressables content...",
                0.95f
            );

            var args = new object[] { null };
            buildMethod.Invoke(null, args);
            var result = args[0];
            var error = resultType
                .GetProperty("Error", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(result)
                ?.ToString();
            if (!string.IsNullOrWhiteSpace(error))
                throw new InvalidOperationException(error);

            Debug.Log("[GoogleSheetImporter] Addressables player content rebuilt.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Addressables rebuild failed after import: {ex.Message}",
                ex
            );
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ----------------------------
    // JSON writer (simple)
    // ----------------------------

    private static string ToJson(object obj)
    {
        if (obj == null)
            return "null";

        if (obj is string s)
            return "\"" + EscapeJson(s) + "\"";

        if (obj is bool b)
            return b ? "true" : "false";

        if (obj is int || obj is long)
            return Convert.ToString(obj, CultureInfo.InvariantCulture);

        if (obj is float f)
            return f.ToString("R", CultureInfo.InvariantCulture);

        if (obj is double d)
            return d.ToString("R", CultureInfo.InvariantCulture);

        if (obj is List<object> list)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                    sb.Append(",");
                sb.Append(ToJson(list[i]));
            }
            sb.Append("]");
            return sb.ToString();
        }

        if (obj is Dictionary<string, object> dict)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            var first = true;
            foreach (var kv in dict)
            {
                if (kv.Value == null)
                    continue;

                if (!first)
                    sb.Append(",");
                first = false;

                sb.Append("\"").Append(EscapeJson(kv.Key)).Append("\":").Append(ToJson(kv.Value));
            }
            sb.Append("}");
            return sb.ToString();
        }

        // fallback
        return "\"" + EscapeJson(Convert.ToString(obj, CultureInfo.InvariantCulture)) + "\"";
    }

    private static string EscapeJson(string s)
    {
        if (s == null)
            return "";

        return s.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
