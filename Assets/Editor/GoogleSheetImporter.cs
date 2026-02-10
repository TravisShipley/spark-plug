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
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

public static class GoogleSheetImporter
{
    private const string OutputPath = "Assets/Data/game_definition.json";
    private const string IndexSheetName = "__Index";

    [MenuItem("SparkPlug/Import From Google Sheet")]
    public static void Import()
    {
        var config = FindConfig();
        if (config == null)
        {
            EditorUtility.DisplayDialog(
                "Import Failed",
                "No GoogleSheetImportConfig found. Create one via Assets > Create > SparkPlug > Google Sheet Import Config, "
                    + "assign the spreadsheet ID (or full URL) and API key, then run the import again.",
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

        try
        {
            var tables = FetchAllTables(spreadsheetId, config.apiKey);
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

            var pack = BuildPack(tables);
            ValidatePack(pack);
            WriteJson(pack);

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Import Complete", $"Wrote {OutputPath}", "OK");
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

    // ----------------------------
    // Config / ID helpers
    // ----------------------------

    private static GoogleSheetImportConfig FindConfig()
    {
        var guids = AssetDatabase.FindAssets("t:GoogleSheetImportConfig");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var config = AssetDatabase.LoadAssetAtPath<GoogleSheetImportConfig>(path);
            if (config != null)
                return config;
        }

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

    // ----------------------------
    // Index-driven import (Sheets API v4)
    // ----------------------------

    private static Dictionary<string, List<Dictionary<string, object>>> FetchAllTables(
        string spreadsheetId,
        string apiKey
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
        var index = ParseIndex(indexCsv);

        // 5) Import tables in order
        var tables = new Dictionary<string, List<Dictionary<string, object>>>(
            StringComparer.OrdinalIgnoreCase
        );

        for (int i = 0; i < index.Count; i++)
        {
            var entry = index[i];
            EditorUtility.DisplayProgressBar(
                "SparkPlug Import",
                $"Importing {entry.TableName} ({i + 1}/{index.Count})",
                index.Count == 0 ? 1f : (i + 1f) / index.Count
            );

            // Find the sheet by name
            var sheet = FindSheetByName(metadata, entry.TableName);
            if (sheet == null)
            {
                throw new InvalidOperationException(
                    $"Sheet '{entry.TableName}' listed in __Index does not exist in the spreadsheet."
                );
            }

            if (!(sheet is Dictionary<string, object> sheetDict))
            {
                Debug.LogWarning(
                    $"[GoogleSheetImporter] Invalid sheet metadata for '{entry.TableName}'. Skipping."
                );
                continue;
            }

            if (
                !sheetDict.TryGetValue("properties", out var propsObj)
                || !(propsObj is Dictionary<string, object> props)
                || !props.TryGetValue("title", out var titleObj)
            )
            {
                Debug.LogWarning(
                    $"[GoogleSheetImporter] Could not get sheet name for '{entry.TableName}'. Skipping."
                );
                continue;
            }

            var sheetName = titleObj?.ToString();
            if (string.IsNullOrEmpty(sheetName))
            {
                Debug.LogWarning(
                    $"[GoogleSheetImporter] Empty sheet name for '{entry.TableName}'. Skipping."
                );
                continue;
            }

            var range = $"'{sheetName}'!A:Z";
            var values = GetSheetValues(spreadsheetId, range, apiKey);

            if (values == null || values.Count == 0)
            {
                Debug.LogWarning(
                    $"[GoogleSheetImporter] Could not download sheet '{entry.TableName}'. Skipping."
                );
                continue;
            }

            var csv = ValuesToCsv(values);
            var rows = ParseCsv(csv);
            if (rows.Count < 1)
                continue;

            var headerRow = rows[0];

            // If the table tab itself is "commented out" (starts with "__"), ignore.
            if (entry.TableName.StartsWith("__", StringComparison.Ordinal))
                continue;

            var dataRows = rows.Skip(1)
                .Where(r => !IsCommentRow(r))
                .Select(r => RowToDict(r, headerRow))
                .Where(d => d.Count > 0)
                .ToList();

            // Meta sheets may be empty but still meaningful.
            if (dataRows.Count > 0 || IsMetaSheet(entry.TableName))
                tables[entry.TableName] = dataRows;
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
            if (string.Equals(title, sheetName, StringComparison.Ordinal))
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
    }

    private static List<IndexEntry> ParseIndex(string indexCsv)
    {
        var rows = ParseCsv(indexCsv);
        if (rows.Count < 2)
            throw new InvalidOperationException(
                "__Index must have a header row and at least one data row."
            );

        var header = rows[0].Select(h => (h ?? "").Trim()).ToList();

        var sheetCol = FindColumnIndex(header, "sheet");
        var enabledCol = FindColumnIndex(header, "enabled");
        var orderCol = FindColumnIndex(header, "order");

        if (sheetCol < 0)
            throw new InvalidOperationException("__Index must include a 'sheet' column.");
        if (enabledCol < 0)
            throw new InvalidOperationException("__Index must include an 'enabled' column.");
        if (orderCol < 0)
            throw new InvalidOperationException("__Index must include an 'order' column.");

        var entries = new List<IndexEntry>();

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
                rawName.StartsWith("__", StringComparison.Ordinal)
                || rawName.StartsWith("//", StringComparison.Ordinal)
            )
                continue;

            if (!IsEnabled(GetCell(row, enabledCol)))
                continue;

            var ord = 0;
            int.TryParse(
                GetCell(row, orderCol),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ord
            );

            entries.Add(new IndexEntry { TableName = rawName, Order = ord });
        }

        // Stable ordering: order asc, then name asc.
        return entries
            .OrderBy(e => e.Order)
            .ThenBy(e => e.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static string GetCell(List<string> row, int col)
    {
        if (col < 0 || col >= row.Count)
            return "";
        return row[col] ?? "";
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

        var first = (row[0] ?? "").Trim();
        return string.IsNullOrWhiteSpace(first)
            || first.StartsWith("_", StringComparison.Ordinal)
            || first.StartsWith("//", StringComparison.Ordinal);
    }

    // ----------------------------
    // Row -> Dict (path nesting)
    // ----------------------------

    private static Dictionary<string, object> RowToDict(List<string> row, List<string> header)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < header.Count && i < row.Count; i++)
        {
            var key = (header[i] ?? "").Trim();
            if (string.IsNullOrEmpty(key))
                continue;

            var val = (row[i] ?? "").Trim();
            if (string.IsNullOrEmpty(val) && !IsRequiredKey(key))
                continue;

            SetByPath(dict, key, val);
        }

        return dict;
    }

    private static bool IsRequiredKey(string key)
    {
        // Keys that we keep even if empty (helps avoid accidental missing critical fields).
        var required = new[] { "id", "gameId", "version", "enabled" };
        var baseKey = key.Split('.')[0];
        return required.Contains(baseKey, StringComparer.OrdinalIgnoreCase);
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

    private static object ParseValue(string s, string path)
    {
        if (s == null)
            return null;

        s = s.Trim();
        if (string.IsNullOrEmpty(s))
            return null;

        if (path.EndsWith("_json", StringComparison.OrdinalIgnoreCase))
        {
            // Strict mode: fail loud if JSON is invalid.
            return ParseJsonLikeValue(s);
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

    private static bool IsMetaSheet(string name) =>
        string.Equals(name, "PackMeta", StringComparison.OrdinalIgnoreCase);

    // ----------------------------
    // Pack building (minimal - extend as needed)
    // ----------------------------

    private static Dictionary<string, object> BuildPack(
        Dictionary<string, List<Dictionary<string, object>>> tables
    )
    {
        var pack = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["gameId"] = "content_pack",
            ["version"] = 2,
            ["numberFormat"] = new Dictionary<string, object>
            {
                ["type"] = "mantissaExponent",
                ["precision"] = 16,
            },
            ["resources"] = new List<object>(),
            ["phases"] = new List<object>(),
            ["zones"] = new List<object>(),
            ["computedVars"] = new List<object>(),
            ["nodes"] = new List<object>(),
            ["nodeInstances"] = new List<object>(),
            ["links"] = new List<object>(),
            ["modifiers"] = new List<object>(),
            ["upgrades"] = new List<object>(),
            ["milestones"] = new List<object>(),
            ["projects"] = new List<object>(),
            ["unlockGraph"] = new List<object>(),
            ["buffs"] = new List<object>(),
        };

        if (tables.TryGetValue("PackMeta", out var metaRows) && metaRows.Count > 0)
        {
            var m = metaRows[0];
            if (m.TryGetValue("gameId", out var gid) && gid != null)
                pack["gameId"] = gid;
            if (m.TryGetValue("version", out var ver) && ver != null)
                pack["version"] = ver;
            if (
                m.TryGetValue("numberFormat", out var nf) && nf is Dictionary<string, object> nfDict
            )
                pack["numberFormat"] = nfDict;
        }

        if (tables.TryGetValue("Resources", out var resRows))
            pack["resources"] = resRows.Select(FlattenNested).Cast<object>().ToList();
        if (tables.TryGetValue("Phases", out var phaseRows))
            pack["phases"] = phaseRows.Select(FlattenNested).Cast<object>().ToList();
        if (tables.TryGetValue("Zones", out var zoneRows))
            pack["zones"] = zoneRows
                .Select(r => FlattenNestedWithArrays(r, "localResources", "tags"))
                .Cast<object>()
                .ToList();

        if (tables.TryGetValue("Nodes", out var nodeRows))
            pack["nodes"] = nodeRows.Select(FlattenNested).Cast<object>().ToList();

        if (
            tables.TryGetValue("NodeOutputs", out var outRows)
            && outRows.Count > 0
            && pack["nodes"] is List<object> nodesList
        )
        {
            var outputsByNode = outRows.GroupBy(r =>
                r.TryGetValue("nodeId", out var v) ? v?.ToString() : null
            );

            // Attach outputs to nodes by matching node id.
            foreach (var nodeObj in nodesList)
            {
                if (nodeObj is not Dictionary<string, object> node)
                    continue;

                if (!node.TryGetValue("id", out var idObj))
                    continue;

                var nodeId = idObj?.ToString();
                if (string.IsNullOrEmpty(nodeId))
                    continue;

                var outputs = outputsByNode
                    .Where(g => string.Equals(g.Key, nodeId, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(g => g)
                    .Select(FlattenNested)
                    .Cast<object>()
                    .ToList();

                if (outputs.Count > 0)
                    node["outputs"] = outputs;
            }
        }

        if (tables.TryGetValue("NodeInstances", out var instRows))
            pack["nodeInstances"] = instRows
                .Select(FlattenNestedWithInitialState)
                .Cast<object>()
                .ToList();

        if (tables.TryGetValue("Modifiers", out var modRows))
            pack["modifiers"] = modRows.Select(FlattenNestedWithScope).Cast<object>().ToList();

        if (tables.TryGetValue("Upgrades", out var upgRows))
            pack["upgrades"] = upgRows.Select(FlattenUpgrade).Cast<object>().ToList();

        if (tables.TryGetValue("Milestones", out var msRows))
            pack["milestones"] = msRows
                .Select(r => FlattenWithJsonRename(r, "grantEffects_json", "grantEffects"))
                .Cast<object>()
                .ToList();

        if (tables.TryGetValue("UnlockGraph", out var ugRows))
            pack["unlockGraph"] = ugRows
                .Select(r =>
                    FlattenWithJsonRename(
                        r,
                        "unlocks_json",
                        "unlocks",
                        "requirements_json",
                        "requirements"
                    )
                )
                .Cast<object>()
                .ToList();

        if (tables.TryGetValue("Prestige", out var pRows) && pRows.Count > 0)
            pack["prestige"] = FlattenNestedWithPrestige(pRows[0]);

        return pack;
    }

    private static Dictionary<string, object> FlattenNested(Dictionary<string, object> flat)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in flat)
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
                continue;
            SetByPath(result, kv.Key, kv.Value);
        }
        return result;
    }

    private static Dictionary<string, object> FlattenNestedWithArrays(
        Dictionary<string, object> flat,
        params string[] arrayKeys
    )
    {
        var d = FlattenNested(flat);
        foreach (var key in arrayKeys)
        {
            if (d.TryGetValue(key, out var v) && v is string s && !string.IsNullOrEmpty(s))
                d[key] = ParseCommaList(s);
        }
        return d;
    }

    private static object ParseCommaList(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return new List<object>();
        return s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => (object)x.Trim().Trim('"', '\'', '[', ']'))
            .ToList();
    }

    private static Dictionary<string, object> FlattenNestedWithInitialState(
        Dictionary<string, object> flat
    )
    {
        var d = FlattenNested(flat);

        if (
            flat.TryGetValue("initialState.level", out var _)
            || flat.TryGetValue("initialState.enabled", out var _2)
        )
        {
            var state = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (flat.TryGetValue("initialState.level", out var lvl) && lvl != null)
                state["level"] = lvl;
            if (flat.TryGetValue("initialState.enabled", out var en) && en != null)
                state["enabled"] = en;

            d["initialState"] = state;
        }

        return d;
    }

    private static Dictionary<string, object> FlattenNestedWithScope(
        Dictionary<string, object> flat
    )
    {
        var d = FlattenNested(flat);

        // If the sheet already provides a nested "scope", we keep it.
        // If it uses scope.* columns, we pack them.
        var hasScopeColumns = flat.Keys.Any(k =>
            k.StartsWith("scope.", StringComparison.OrdinalIgnoreCase)
        );
        if (!hasScopeColumns)
            return d;

        var scope = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in flat)
        {
            if (!kv.Key.StartsWith("scope.", StringComparison.OrdinalIgnoreCase))
                continue;

            var key = kv.Key.Substring("scope.".Length);
            scope[key] = kv.Value;
        }

        d["scope"] = scope;
        return d;
    }

    private static Dictionary<string, object> FlattenUpgrade(Dictionary<string, object> flat)
    {
        var d = FlattenNestedWithArrays(flat, "tags");
        RenameKey(d, "cost_json", "cost");
        RenameKey(d, "effects_json", "effects");
        RenameKey(d, "requirements_json", "requirements");
        return d;
    }

    private static Dictionary<string, object> FlattenWithJsonRename(
        Dictionary<string, object> flat,
        params string[] renames
    )
    {
        var d = FlattenNested(flat);
        for (int i = 0; i + 1 < renames.Length; i += 2)
            RenameKey(d, renames[i], renames[i + 1]);
        return d;
    }

    private static void RenameKey(Dictionary<string, object> d, string from, string to)
    {
        if (d.TryGetValue(from, out var v))
        {
            d.Remove(from);
            d[to] = v;
        }
    }

    private static Dictionary<string, object> FlattenNestedWithPrestige(
        Dictionary<string, object> flat
    )
    {
        var d = FlattenNested(flat);
        RenameKey(d, "metaUpgrades_json", "metaUpgrades");
        return d;
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
    }

    private static void WriteJson(Dictionary<string, object> pack)
    {
        var dir = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = ToJson(pack);
        File.WriteAllText(OutputPath, json, Encoding.UTF8);
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
