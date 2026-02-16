using System;
using System.Collections.Generic;
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

            // Fail loud on missing required roots.
            if (gd.nodes == null || gd.nodes.Count == 0)
                throw new InvalidOperationException("GameDefinition: 'nodes' is missing or empty.");

            if (gd.nodeInstances == null || gd.nodeInstances.Count == 0)
                throw new InvalidOperationException(
                    "GameDefinition: 'nodeInstances' is missing or empty."
                );

            // Optional roots (may be empty, but should not be null if present in schema).
            if (gd.upgrades == null)
                gd.upgrades = new List<UpgradeEntry>();

            if (gd.modifiers == null)
                gd.modifiers = new List<ModifierEntry>();

            if (gd.nodeInputs == null)
                gd.nodeInputs = new List<NodeInputDefinition>();

            if (gd.milestones == null)
                gd.milestones = new List<MilestoneEntry>();

            GameDefinitionValidator.Validate(gd);
            return gd;
        }
        catch (Exception ex)
        {
            var wrapped = new InvalidOperationException(
                "Failed to parse/validate game definition JSON: " + ex.Message,
                ex
            );

            Debug.LogError(wrapped.Message);

#if UNITY_EDITOR
            // Fail loud while iterating on content: stop entering play mode on invalid packs.
            if (UnityEditor.EditorApplication.isPlaying)
                UnityEditor.EditorApplication.isPlaying = false;
#endif

            throw wrapped;
        }
    }
}
