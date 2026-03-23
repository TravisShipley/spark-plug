using UnityEngine;

[CreateAssetMenu(fileName = "GameSessionConfig", menuName = "SparkPlug/Boot/Game Session Config")]
public sealed class GameSessionConfigAsset : ScriptableObject
{
    [Header("Identity")]
    [SerializeField]
    private string sessionId;

    [SerializeField]
    private string displayName;

    [Header("Boot")]
    [SerializeField]
    private string sceneName;

    [SerializeField]
    private TextAsset gameDefinitionJson;

    [Header("Save")]
    [SerializeField]
    private string saveSlotId = "default";

    [SerializeField]
    private bool resetSaveOnBoot;

    [SerializeField]
    private bool verboseLogging;

    public string SessionId => Normalize(sessionId);
    public string DisplayName => Normalize(displayName);
    public string SceneName => Normalize(sceneName);
    public TextAsset GameDefinitionJson => gameDefinitionJson;
    public string SaveSlotId =>
        string.IsNullOrWhiteSpace(saveSlotId) ? "default" : Normalize(saveSlotId);
    public bool ResetSaveOnBoot => resetSaveOnBoot;
    public bool VerboseLogging => verboseLogging;

    public GameSessionRequest ToRequest()
    {
        return GameSessionRequest.FromConfig(this);
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        sessionId = Normalize(sessionId);
        displayName = Normalize(displayName);
        sceneName = Normalize(sceneName);
        saveSlotId = string.IsNullOrWhiteSpace(saveSlotId) ? "default" : Normalize(saveSlotId);
    }
#endif
}
