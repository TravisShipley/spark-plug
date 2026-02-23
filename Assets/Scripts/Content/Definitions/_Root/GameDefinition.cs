using System;
using System.Collections.Generic;

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
