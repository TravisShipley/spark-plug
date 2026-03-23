using System;

public sealed class SparkPlugRuntimeConfig
{
    public SparkPlugRuntimeConfig(
        string sessionId,
        string displayName,
        string saveSlotId,
        bool resetSaveOnBoot,
        bool verboseLogging,
        GameDefinition definition
    )
    {
        SessionId = string.IsNullOrWhiteSpace(sessionId) ? "default" : sessionId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? SessionId : displayName.Trim();
        SaveSlotId = string.IsNullOrWhiteSpace(saveSlotId) ? "default" : saveSlotId.Trim();
        ResetSaveOnBoot = resetSaveOnBoot;
        VerboseLogging = verboseLogging;
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public string SessionId { get; }
    public string DisplayName { get; }
    public string SaveSlotId { get; }
    public bool ResetSaveOnBoot { get; }
    public bool VerboseLogging { get; }
    public GameDefinition Definition { get; }
}
