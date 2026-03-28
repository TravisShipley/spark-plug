using System;
using System.Collections.Generic;

[Serializable]
public sealed class GameDefinition
{
    public List<ResourceDefinition> resources = new List<ResourceDefinition>();
    public List<ZoneDefinition> zones = new List<ZoneDefinition>();
    public List<ComputedVarDefinition> computedVars = new List<ComputedVarDefinition>();
    public List<StateVarDefinition> stateVars = new List<StateVarDefinition>();
    public List<NodeDefinition> nodes = new List<NodeDefinition>();
    public List<NodeInputDefinition> nodeInputs = new List<NodeInputDefinition>();
    public List<NodeStateCapacityDefinition> nodeStateCapacities =
        new List<NodeStateCapacityDefinition>();
    public List<NodeInstanceDefinition> nodeInstances = new List<NodeInstanceDefinition>();
    public List<UnlockGraphDefinition> unlockGraph = new List<UnlockGraphDefinition>();
    public List<ModifierDefinition> modifiers = new List<ModifierDefinition>();
    public List<UpgradeDefinition> upgrades = new List<UpgradeDefinition>();
    public List<MilestoneDefinition> milestones = new List<MilestoneDefinition>();
    public List<BuffDefinition> buffs = new List<BuffDefinition>();
    public List<BuyModeDefinition> buyModes = new List<BuyModeDefinition>();
    public List<TriggerDefinition> triggers = new List<TriggerDefinition>();
    public List<RewardPoolDefinition> rewardPools = new List<RewardPoolDefinition>();
    public PrestigeDefinition prestige;
}
