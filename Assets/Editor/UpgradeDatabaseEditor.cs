#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UpgradeDatabase))]
public class UpgradeDatabaseEditor : Editor
{
    private TextAsset csv;
    private string sheetUrl = "https://docs.google.com/spreadsheets/d/1rNnpV_e03JQWv_FW6qBzVpAPWgjHFIsjvsF1jeTXNOk/edit?gid=183983359#gid=183983359";
    private readonly Dictionary<int, bool> foldoutByInstanceId = new Dictionary<int, bool>();

    public override void OnInspectorGUI()
    {
        var db = (UpgradeDatabase)target;

        EditorGUILayout.LabelField("Upgrades", EditorStyles.boldLabel);

        var upgradesProp = serializedObject.FindProperty("upgrades");
        if (upgradesProp != null)
        {
            EditorGUI.indentLevel++;

            for (int i = 0; i < upgradesProp.arraySize; i++)
            {
                var element = upgradesProp.GetArrayElementAtIndex(i);
                var def = element.objectReferenceValue as UpgradeDefinition;
                if (def == null)
                    continue;

                def.hideFlags = HideFlags.None;

                int key = def.GetInstanceID();
                foldoutByInstanceId.TryGetValue(key, out bool isOpen);

                // Compact header label for the foldout
                string headerLabel = string.IsNullOrWhiteSpace(def.Id)
                    ? def.name
                    : def.Id;

                EditorGUILayout.BeginVertical("box");

                isOpen = EditorGUILayout.Foldout(isOpen, headerLabel, true);
                foldoutByInstanceId[key] = isOpen;

                if (isOpen)
                {
                    // Draw fields explicitly (no PropertyField) so `[Header(...)]` decorators from UpgradeDefinition don't show up.
                    def.Id = EditorGUILayout.TextField("Id", def.Id);
                    def.name = def.Id; // keep sub-asset name aligned

                    def.DisplayName = EditorGUILayout.TextField("Display Name", def.DisplayName);
                    def.GeneratorId = EditorGUILayout.TextField("Generator Id", def.GeneratorId);
                    def.Cost = EditorGUILayout.DoubleField("Cost", def.Cost);
                    def.EffectType = (UpgradeEffectType)EditorGUILayout.EnumPopup("Effect Type", def.EffectType);
                    def.Value = EditorGUILayout.DoubleField("Value", def.Value);

                    EditorUtility.SetDirty(def);
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUI.indentLevel--;
        }
        else
        {
            EditorGUILayout.HelpBox("No upgrades found.", MessageType.Info);
        }

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("CSV Import", EditorStyles.boldLabel);

        csv = (TextAsset)EditorGUILayout.ObjectField("CSV File", csv, typeof(TextAsset), false);
        using (new EditorGUI.DisabledScope(csv == null))
        {
            if (GUILayout.Button("Import CSV TextAsset"))
                ImportText((UpgradeDatabase)target, csv.text);
        }

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Google Sheets Import (Public Link)", EditorStyles.boldLabel);

        sheetUrl = EditorGUILayout.TextField("Sheet URL", sheetUrl);

        if (GUILayout.Button("Import From Sheet URL"))
        {
            _ = ImportFromSheetUrlAsync((UpgradeDatabase)target, sheetUrl);
        }

        EditorGUILayout.HelpBox(
            "Expected headers:\nId,DisplayName,GeneratorId,Cost,EffectType,Value\n\n" +
            "Sheet must be readable by anyone with the link (Viewer) for Option 1.",
            MessageType.Info);

        serializedObject.ApplyModifiedProperties();
    }

    private static void ImportText(UpgradeDatabase db, string csvText)
    {
        try
        {
            var rows = ParseCsv(csvText);
            if (rows.Count == 0)
            {
                Debug.LogWarning("UpgradeDatabase import: CSV has no rows.");
                return;
            }

            var header = rows[0];
            var col = BuildHeaderMap(header);

            string Get(IReadOnlyList<string> r, string name)
                => col.TryGetValue(name, out int idx) && idx < r.Count ? r[idx] : "";

            var list = new List<UpgradeDefinition>();

            for (int i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Count == 0) continue;

                string id = Get(r, "Id").Trim();
                if (string.IsNullOrWhiteSpace(id)) continue;

                string displayName = Get(r, "DisplayName").Trim();
                string generatorId = Get(r, "GeneratorId").Trim();

                double cost = ParseDouble(Get(r, "Cost"));

                string effectTypeRaw = Get(r, "EffectType").Trim();
                string valueRaw = Get(r, "Value").Trim();

                if (cost < 0) cost = 0;

                // Parse effect type (defaults to OutputMultiplier if missing/invalid)
                UpgradeEffectType effectType;
                if (!Enum.TryParse(effectTypeRaw, ignoreCase: true, out effectType))
                    effectType = UpgradeEffectType.OutputMultiplier;

                double value = ParseDouble(valueRaw);
                if (value < 1.0) value = 1.0;

                var def = ScriptableObject.CreateInstance<UpgradeDefinition>();
                def.name = id;
                def.Id = id;
                def.DisplayName = displayName;
                def.GeneratorId = generatorId;
                def.Cost = cost;
                def.EffectType = effectType;
                def.Value = value;

                list.Add(def);
            }

            Undo.RecordObject(db, "Import Upgrades");

            // Clear existing upgrade definition sub-assets
            string dbPath = AssetDatabase.GetAssetPath(db);
            if (!string.IsNullOrEmpty(dbPath))
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath(dbPath);
                foreach (var a in assets)
                {
                    if (a == null) continue;
                    if (a == db) continue;
                    if (a is UpgradeDefinition)
                    {
                        Undo.DestroyObjectImmediate(a);
                    }
                }
            }

            // Add new definitions as sub-assets so they persist without creating hundreds of separate assets
            foreach (var def in list)
            {
                def.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(def, db);
            }

            db.ReplaceAll(list);
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(dbPath);

            Debug.Log($"UpgradeDatabase import: Imported {list.Count} upgrades into '{db.name}'.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"UpgradeDatabase import failed: {ex}");
        }
    }

    private static async Task ImportFromSheetUrlAsync(UpgradeDatabase db, string url)
    {
        try
        {
            string exportUrl = ToCsvExportUrl(url);

            using var client = new HttpClient();
            string text = await client.GetStringAsync(exportUrl);

            // Common failure: the sheet isn't public, so you get HTML instead of CSV.
            if (text.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("<html", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError("UpgradeDatabase import: Downloaded HTML instead of CSV. Make sure the sheet is shared as 'Anyone with the link' can view.");
                return;
            }

            ImportText(db, text);
        }
        catch (Exception ex)
        {
            Debug.LogError($"UpgradeDatabase import from sheet failed: {ex.Message}\n{ex}");
        }
    }

    private static string ToCsvExportUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Sheet URL is empty.");

        // If user already pasted an export URL, keep it.
        if (input.Contains("/export?", StringComparison.OrdinalIgnoreCase))
            return input.Trim();

        // Parse /d/{spreadsheetId}/
        var uri = new Uri(input);
        var parts = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        int dIndex = Array.IndexOf(parts, "d");
        if (dIndex < 0 || dIndex + 1 >= parts.Length)
            throw new ArgumentException("Could not find spreadsheet id in URL.");

        string sheetId = parts[dIndex + 1];

        // Parse gid from query or fragment (#gid=...)
        string gid = null;
        var query = uri.Query ?? "";
        var fragment = uri.Fragment ?? "";

        gid = GetQueryParam(query, "gid") ?? GetQueryParam(fragment.TrimStart('#'), "gid");

        // If no gid, default to 0 (first sheet)
        if (string.IsNullOrWhiteSpace(gid))
            gid = "0";

        return $"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=csv&gid={gid}";
    }

    private static string GetQueryParam(string queryLike, string key)
    {
        if (string.IsNullOrWhiteSpace(queryLike)) return null;

        // Accept both "a=b&c=d" and "?a=b&c=d"
        string q = queryLike.TrimStart('?');
        var pairs = q.Split('&');

        foreach (var p in pairs)
        {
            var kv = p.Split(new[] { '=' }, 2);
            if (kv.Length != 2) continue;
            if (string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]);
        }

        return null;
    }

    private static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            var k = (header[i] ?? "").Trim();
            if (string.IsNullOrWhiteSpace(k)) continue;
            if (!map.ContainsKey(k)) map.Add(k, i);
        }
        return map;
    }

    private static double ParseDouble(string s)
    {
        s = (s ?? "").Trim();
        if (string.IsNullOrEmpty(s)) return 0;

        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;

        return 0;
    }

    // Minimal CSV parser with quotes support
    private static List<List<string>> ParseCsv(string text)
    {
        var result = new List<List<string>>();
        using var reader = new StringReader(text);

        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.TrimStart().StartsWith("#")) continue;
            result.Add(ParseCsvLine(line));
        }

        return result;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        if (line == null) return fields;

        bool inQuotes = false;
        var current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Length = 0;
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
#endif