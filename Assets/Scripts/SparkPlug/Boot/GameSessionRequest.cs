using System;

public sealed class GameSessionRequest
{
    public GameSessionRequest(
        string sessionId,
        string displayName,
        string definitionJson,
        string saveSlotId,
        bool resetSaveOnBoot,
        bool verboseLogging
    )
    {
        SessionId = Require(sessionId, nameof(sessionId));
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? SessionId : displayName.Trim();
        DefinitionJson = Require(definitionJson, nameof(definitionJson));
        SaveSlotId = string.IsNullOrWhiteSpace(saveSlotId) ? "default" : saveSlotId.Trim();
        ResetSaveOnBoot = resetSaveOnBoot;
        VerboseLogging = verboseLogging;
    }

    public string SessionId { get; }
    public string DisplayName { get; }
    public string DefinitionJson { get; }
    public string SaveSlotId { get; }
    public bool ResetSaveOnBoot { get; }
    public bool VerboseLogging { get; }

    public static GameSessionRequest FromConfig(GameSessionConfigAsset config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.SessionId))
        {
            throw new InvalidOperationException(
                $"GameSessionRequest: '{config.name}' is missing a sessionId."
            );
        }

        if (config.GameDefinitionJson == null)
        {
            throw new InvalidOperationException(
                $"GameSessionRequest: '{config.name}' is missing a game definition TextAsset."
            );
        }

        return new GameSessionRequest(
            config.SessionId,
            config.DisplayName,
            config.GameDefinitionJson.text,
            config.SaveSlotId,
            config.ResetSaveOnBoot,
            config.VerboseLogging
        );
    }

    private static string Require(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} is required.", paramName);

        return value.Trim();
    }
}
