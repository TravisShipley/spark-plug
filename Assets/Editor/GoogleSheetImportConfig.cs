using UnityEngine;

[CreateAssetMenu(
    menuName = "SparkPlug/Google Sheet Import Config",
    fileName = "GoogleSheetImportConfig"
)]
public sealed class GoogleSheetImportConfig : ScriptableObject
{
    [Header("Google Sheet")]
    public string spreadsheetId;

    [Header("Sheets API v4")]
    public string apiKey;

    [Header("Output")]
    public string outputJsonPath = "Assets/Data/Definitions/game_definition.json";
    public string resourcesFallbackOutputPath;
    public string addressableKey;
    public bool rebuildAddressables;
}
