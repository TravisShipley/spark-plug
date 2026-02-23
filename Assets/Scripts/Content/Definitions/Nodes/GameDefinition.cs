using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class GameDefinition
{
    public List<ResourceDefinition> resources = new List<ResourceDefinition>();
    public List<ComputedVarDefinition> computedVars = new List<ComputedVarDefinition>();
    public List<NodeDefinition> nodes = new List<NodeDefinition>();
    public List<NodeInputDefinition> nodeInputs = new List<NodeInputDefinition>();
    public List<NodeInstanceDefinition> nodeInstances = new List<NodeInstanceDefinition>();
    public List<UnlockGraphEntry> unlockGraph = new List<UnlockGraphEntry>();
    public List<ModifierEntry> modifiers = new List<ModifierEntry>();
    public List<UpgradeEntry> upgrades = new List<UpgradeEntry>();
    public List<MilestoneEntry> milestones = new List<MilestoneEntry>();
    public List<BuffDefinition> buffs = new List<BuffDefinition>();
    public List<TriggerDefinition> triggers = new List<TriggerDefinition>();
    public List<RewardPoolDefinition> rewardPools = new List<RewardPoolDefinition>();
    public PrestigeDefinition prestige;
}

[Serializable]
public sealed class TriggerDefinition
{
    public string id;
    public string @event;
    public string eventType;
    public TriggerScopeDefinition scope;
    public TriggerConditionDefinition[] conditions;
    public TriggerActionDefinition[] actions;
}

[Serializable]
public sealed class TriggerScopeDefinition
{
    public string kind;
    public string zoneId;
    public string nodeId;
    public string nodeTag;
    public string resource;
}

[Serializable]
public sealed class TriggerConditionDefinition
{
    public string type;
    public TriggerConditionArgsDefinition args;
}

[Serializable]
public sealed class TriggerConditionArgsDefinition
{
    public string milestoneId;
}

[Serializable]
public sealed class TriggerActionDefinition
{
    public string type;
    public string rewardPoolId;
}

[Serializable]
public sealed class RewardPoolDefinition
{
    public string id;
    public RewardEntryDefinition[] rewards;
}

[Serializable]
public sealed class RewardEntryDefinition
{
    public float weight;
    public RewardActionDefinition action;
}

[Serializable]
public sealed class RewardActionDefinition
{
    public string type;
    public string resourceId;
    public double amount;
}

[Serializable]
public sealed class ResourceDefinition
{
    public string id;
    public string displayName;
    public string kind;
    public ResourceFormat format;
}

[Serializable]
public sealed class ResourceFormat
{
    public string style;
    public string symbol;
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

    public bool enabled = true;
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

[Serializable]
public sealed class MilestoneEntry
{
    public string id;
    public string nodeId;
    public string zoneId;
    public int atLevel;
    public EffectItem[] grantEffects;
}

[Serializable]
public sealed class UnlockGraphEntry
{
    public string id;
    public string zoneId;
    public string targetNodeInstanceId;
    public UnlockRequirement[] requirements;

    // Legacy compatibility for existing packs that still use "unlocks": [{ kind, id }].
    public UnlockTarget[] unlocks;
}

[Serializable]
public sealed class UnlockTarget
{
    public string kind;
    public string id;
}

[Serializable]
public sealed class UnlockRequirement
{
    public string type;
    public string nodeInstanceId;
    public int minLevel;
    public string upgradeId;
    public UnlockRequirementArgs args;
}

[Serializable]
public sealed class UnlockRequirementArgs
{
    public string nodeInstanceId;
    public int minLevel;
    public string upgradeId;
    public string id;
    public int level;
}

[Serializable]
public sealed class BuffDefinition
{
    public string id;
    public string displayName;
    public string zoneId;
    public int durationSeconds;
    public string stacking;
    public EffectItem[] effects;

    // Optional raw import path; loader may normalize into effects.
    public string effects_json;
    public string[] tags;
}

[Serializable]
public sealed class ComputedVarDefinition
{
    public string id;
    public string[] dependsOn;
}

[Serializable]
public sealed class PrestigeDefinition
{
    public PrestigeFormula formula;
    public MetaUpgradeDefinition[] metaUpgrades;
}

[Serializable]
public sealed class PrestigeFormula
{
    public string basedOn;
}

[Serializable]
public sealed class MetaUpgradeDefinition
{
    public string id;
    public ComputedFormula computed;
    public string writesToState;
}

[Serializable]
public sealed class ComputedFormula
{
    public string basedOn;
}
