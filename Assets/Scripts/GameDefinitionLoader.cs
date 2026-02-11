using System;
using System.IO;
using UnityEngine;

public static class GameDefinitionLoader
{
    public static GameDefinition LoadFromFile(
        string projectRelativePath = "Assets/Data/game_definition.json"
    )
    {
        var full = Path.GetFullPath(projectRelativePath);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Game definition file not found: {full}");

        var json = File.ReadAllText(full);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"Game definition file is empty: {full}");

        try
        {
            // UnityEngine.JsonUtility expects the JSON to map to the class.
            var gd = JsonUtility.FromJson<GameDefinition>(json);
            if (gd == null)
                throw new InvalidOperationException("Failed to deserialize game definition JSON.");

            return gd;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to parse game definition JSON: " + ex.Message,
                ex
            );
        }
    }
}
