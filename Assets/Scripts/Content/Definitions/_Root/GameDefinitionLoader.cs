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
                gd.upgrades = new List<UpgradeDefinition>();

            if (gd.modifiers == null)
                gd.modifiers = new List<ModifierDefinition>();

            if (gd.nodeInputs == null)
                gd.nodeInputs = new List<NodeInputDefinition>();

            if (gd.unlockGraph == null)
                gd.unlockGraph = new List<UnlockGraphDefinition>();

            if (gd.milestones == null)
                gd.milestones = new List<MilestoneDefinition>();

            if (gd.buffs == null)
                gd.buffs = new List<BuffDefinition>();

            if (gd.buyModes == null)
                gd.buyModes = new List<BuyModeDefinition>();

            if (gd.triggers == null)
                gd.triggers = new List<TriggerDefinition>();

            if (gd.rewardPools == null)
                gd.rewardPools = new List<RewardPoolDefinition>();

            if (gd.computedVars == null)
                gd.computedVars = new List<ComputedVarDefinition>();

            NormalizeBuffEffects(gd.buffs);
            NormalizeParameterizedPaths(gd);

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

    private static void NormalizeBuffEffects(IReadOnlyList<BuffDefinition> buffs)
    {
        if (buffs == null || buffs.Count == 0)
            return;

        for (int i = 0; i < buffs.Count; i++)
        {
            var buff = buffs[i];
            if (buff == null)
                continue;

            if (buff.effects != null && buff.effects.Length > 0)
                continue;

            var rawEffectsJson = (buff.effects_json ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(rawEffectsJson))
                continue;

            buff.effects = ParseEffectsJson(rawEffectsJson, buff.id);
        }
    }

    private static EffectItem[] ParseEffectsJson(string effectsJson, string buffId)
    {
        try
        {
            var raw = (effectsJson ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(raw))
                return Array.Empty<EffectItem>();

            if (raw.StartsWith("[", StringComparison.Ordinal))
            {
                var wrapped = "{\"items\":" + raw + "}";
                var list = JsonUtility.FromJson<EffectItemList>(wrapped);
                return list?.items ?? Array.Empty<EffectItem>();
            }

            var single = JsonUtility.FromJson<EffectItem>(raw);
            if (single != null && !string.IsNullOrWhiteSpace(single.modifierId))
                return new[] { single };

            var parsedList = JsonUtility.FromJson<EffectItemList>(raw);
            return parsedList?.items ?? Array.Empty<EffectItem>();
        }
        catch (Exception ex)
        {
            var id = string.IsNullOrWhiteSpace(buffId) ? "unknown" : buffId.Trim();
            throw new InvalidOperationException(
                $"Buff '{id}' has invalid effects_json. Ensure it is valid JSON for effects[].modifierId. {ex.Message}"
            );
        }
    }

    [Serializable]
    private sealed class EffectItemList
    {
        public EffectItem[] items;
    }

    private static void NormalizeParameterizedPaths(GameDefinition definition)
    {
        if (definition == null)
            return;

        if (definition.modifiers != null)
        {
            for (int i = 0; i < definition.modifiers.Count; i++)
            {
                var modifier = definition.modifiers[i];
                if (modifier == null)
                    continue;

                var rawTarget = (modifier.target ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(rawTarget))
                    continue;

                if (
                    !ParameterizedPathParser.TryCanonicalizeModifierParameterizedPath(
                        rawTarget,
                        out var canonicalTarget,
                        out var usedLegacyFormat
                    )
                )
                {
                    continue;
                }

                if (string.Equals(rawTarget, canonicalTarget, StringComparison.Ordinal))
                    continue;

                modifier.target = canonicalTarget;

#if UNITY_EDITOR
                if (usedLegacyFormat)
                {
                    Debug.LogWarning(
                        $"GameDefinitionLoader: modifier '{modifier.id}' target '{rawTarget}' normalized to '{canonicalTarget}'. Prefer bracket form."
                    );
                }
#endif
            }
        }

        if (definition.computedVars != null)
        {
            for (int i = 0; i < definition.computedVars.Count; i++)
            {
                var computedVar = definition.computedVars[i];
                if (computedVar?.dependsOn == null)
                    continue;

                for (int d = 0; d < computedVar.dependsOn.Length; d++)
                {
                    var raw = (computedVar.dependsOn[d] ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(raw))
                        continue;

                    if (
                        !ParameterizedPathParser.TryCanonicalizeFormulaParameterizedPath(
                            raw,
                            out var canonical,
                            out var usedLegacyFormat
                        )
                    )
                    {
                        continue;
                    }

                    if (string.Equals(raw, canonical, StringComparison.Ordinal))
                        continue;

                    computedVar.dependsOn[d] = canonical;

#if UNITY_EDITOR
                    if (usedLegacyFormat)
                    {
                        Debug.LogWarning(
                            $"GameDefinitionLoader: computedVars[{i}] dependsOn '{raw}' normalized to '{canonical}'. Prefer bracket form."
                        );
                    }
#endif
                }
            }
        }

        if (definition.prestige?.formula != null)
        {
            NormalizeFormulaPath(
                ref definition.prestige.formula.basedOn,
                "prestige.formula.basedOn"
            );
        }

        var metaUpgrades = definition.prestige?.metaUpgrades;
        if (metaUpgrades == null)
            return;

        for (int i = 0; i < metaUpgrades.Length; i++)
        {
            var metaUpgrade = metaUpgrades[i];
            if (metaUpgrade?.computed == null)
                continue;

            NormalizeFormulaPath(
                ref metaUpgrade.computed.basedOn,
                $"prestige.metaUpgrades[{i}].computed.basedOn"
            );
        }
    }

    private static void NormalizeFormulaPath(ref string path, string context)
    {
        var raw = (path ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
            return;

        if (
            !ParameterizedPathParser.TryCanonicalizeFormulaParameterizedPath(
                raw,
                out var canonical,
                out var usedLegacyFormat
            )
        )
        {
            return;
        }

        if (string.Equals(raw, canonical, StringComparison.Ordinal))
            return;

        path = canonical;

#if UNITY_EDITOR
        if (usedLegacyFormat)
        {
            Debug.LogWarning(
                $"GameDefinitionLoader: {context} '{raw}' normalized to '{canonical}'. Prefer bracket form."
            );
        }
#endif
    }
}
