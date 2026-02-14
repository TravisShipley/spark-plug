using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class GameDefinition
{
    public List<NodeDefinition> nodes = new List<NodeDefinition>();
    public List<NodeInstanceDefinition> nodeInstances = new List<NodeInstanceDefinition>();
    public List<ModifierEntry> modifiers = new List<ModifierEntry>();
    public List<UpgradeEntry> upgrades = new List<UpgradeEntry>();
}

[Serializable]
public sealed class UpgradeEntry
{
    public string id;
    public string displayName;

    public Presentation presentation;

    // Category: upgrade|research|card
    public string category;

    // Zone this upgrade belongs to (optional)
    public string zoneId;

    // Cost is an array of resource-amount pairs per schema
    public CostItem[] cost;

    public bool repeatable;
    public int maxRank;
    public RankCostScaling rankCostScaling;

    // Effects: array of effect descriptors (minimal: modifierId)
    public EffectItem[] effects;

    // Requirements: array of requirement descriptors. 'args' preserved as raw JSON string
    public RequirementItem[] requirements;

    public string[] tags;

    // Legacy/simple fields (kept for backward compatibility if needed)
    public string generatorId;
    public double costSimple;

    public bool enabled = true;
    public UpgradeEffectType effectType;
    public double value;
}

[Serializable]
public sealed class Presentation
{
    public string descriptionKey;
    public string iconId;
    public string imageId;
}

[Serializable]
public sealed class CostItem
{
    public string resource;
    public string amount;
}

[Serializable]
public sealed class RankCostScaling
{
    public string type;
    public string basePriceMultiplier;
    public string growth;
}

[Serializable]
public sealed class EffectItem
{
    public string modifierId;
}

[Serializable]
public sealed class RequirementItem
{
    public string type;
    public string args; // raw JSON for args
}

[Serializable]
public sealed class ModifierEntry
{
    public string id;
    public string source;
    public string zoneId;
    public ModifierScope scope;
    public string operation;
    public string target;
    public double value;
}

[Serializable]
public sealed class ModifierScope
{
    public string kind;
    public string zoneId;
    public string nodeId;
    public string nodeTag;
    public string resource;
}
