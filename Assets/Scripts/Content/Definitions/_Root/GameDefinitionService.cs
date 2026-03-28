using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class GameDefinitionService
{
    public const string DefaultPath = GameDefinitionLoader.DefaultFilePath;
    private GameDefinition definition;
    private readonly HashSet<string> warnedNodeInputsByNodeId = new(StringComparer.Ordinal);
    private ResourceCatalog resourceCatalog;
    private NodeCatalog nodeCatalog;
    private NodeInputCatalog nodeInputCatalog;
    private NodeInstanceCatalog nodeInstanceCatalog;
    private UpgradeCatalog upgradeCatalog;
    private BuffCatalog buffCatalog;
    private BuyModeCatalog buyModeCatalog;

    public GameDefinitionService(GameDefinition loadedDefinition)
    {
        Reload(loadedDefinition);
    }

    public void Reload(GameDefinition loadedDefinition)
    {
        definition =
            loadedDefinition
            ?? throw new InvalidOperationException(
                "GameDefinitionService: loadedDefinition is null."
            );
        resourceCatalog = new ResourceCatalog(definition.resources);
        nodeCatalog = new NodeCatalog(definition.nodes);
        nodeInputCatalog = new NodeInputCatalog(definition);
        nodeInstanceCatalog = new NodeInstanceCatalog(definition.nodeInstances);
        upgradeCatalog = new UpgradeCatalog(definition.upgrades);
        buffCatalog = new BuffCatalog(definition.buffs);
        buyModeCatalog = new BuyModeCatalog(definition.buyModes);
        WarnForNodeInputsNotExecuted();
    }

    public IReadOnlyList<ResourceDefinition> Resources =>
        resourceCatalog?.Resources ?? Array.Empty<ResourceDefinition>();
    public IReadOnlyList<ZoneDefinition> Zones =>
        (IReadOnlyList<ZoneDefinition>)definition.zones ?? Array.Empty<ZoneDefinition>();
    public IReadOnlyList<StateVarDefinition> StateVars =>
        (IReadOnlyList<StateVarDefinition>)definition.stateVars ?? Array.Empty<StateVarDefinition>();
    public IReadOnlyList<NodeStateCapacityDefinition> NodeStateCapacities =>
        (IReadOnlyList<NodeStateCapacityDefinition>)definition.nodeStateCapacities
        ?? Array.Empty<NodeStateCapacityDefinition>();
    public IReadOnlyList<NodeDefinition> Nodes =>
        nodeCatalog?.Nodes ?? Array.Empty<NodeDefinition>();
    public IReadOnlyList<NodeInputDefinition> NodeInputs =>
        nodeInputCatalog?.NodeInputs ?? Array.Empty<NodeInputDefinition>();
    public IReadOnlyList<NodeInstanceDefinition> NodeInstances =>
        nodeInstanceCatalog?.NodeInstances ?? Array.Empty<NodeInstanceDefinition>();
    public IReadOnlyList<UnlockGraphDefinition> UnlockGraph =>
        (IReadOnlyList<UnlockGraphDefinition>)definition.unlockGraph
        ?? Array.Empty<UnlockGraphDefinition>();
    public IReadOnlyList<ModifierDefinition> Modifiers =>
        (IReadOnlyList<ModifierDefinition>)definition.modifiers
        ?? Array.Empty<ModifierDefinition>();
    public IReadOnlyList<UpgradeDefinition> Upgrades =>
        upgradeCatalog?.Upgrades ?? Array.Empty<UpgradeDefinition>();
    public IReadOnlyList<BuffDefinition> Buffs =>
        buffCatalog?.Buffs ?? Array.Empty<BuffDefinition>();
    public IReadOnlyList<BuyModeDefinition> BuyModes =>
        buyModeCatalog?.All ?? Array.Empty<BuyModeDefinition>();
    public IReadOnlyList<MilestoneDefinition> Milestones =>
        (IReadOnlyList<MilestoneDefinition>)definition.milestones
        ?? Array.Empty<MilestoneDefinition>();
    public IReadOnlyList<TriggerDefinition> Triggers =>
        (IReadOnlyList<TriggerDefinition>)definition.triggers ?? Array.Empty<TriggerDefinition>();
    public IReadOnlyList<RewardPoolDefinition> RewardPools =>
        (IReadOnlyList<RewardPoolDefinition>)definition.rewardPools
        ?? Array.Empty<RewardPoolDefinition>();

    public bool TryGetNode(string id, out NodeDefinition node)
    {
        if (nodeCatalog == null)
        {
            node = null;
            return false;
        }

        return nodeCatalog.TryGet(id, out node);
    }

    public bool TryGetNodeInstance(string id, out NodeInstanceDefinition nodeInstance)
    {
        if (nodeInstanceCatalog == null)
        {
            nodeInstance = null;
            return false;
        }

        return nodeInstanceCatalog.TryGet(id, out nodeInstance);
    }

    public bool TryGetUpgrade(string id, out UpgradeDefinition entry)
    {
        if (upgradeCatalog == null)
        {
            entry = null;
            return false;
        }

        return upgradeCatalog.TryGet(id, out entry);
    }

    public IReadOnlyList<ModifierDefinition> ResolveUpgradeModifiers(string upgradeId)
    {
        var id = (upgradeId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(id))
            return Array.Empty<ModifierDefinition>();

        if (!TryGetUpgrade(id, out var upgrade) || upgrade == null)
            return Array.Empty<ModifierDefinition>();

        var modifiers =
            (IReadOnlyList<ModifierDefinition>)definition.modifiers
            ?? Array.Empty<ModifierDefinition>();
        var modifiersById = modifiers
            .Where(m => m != null && !string.IsNullOrWhiteSpace(m.id))
            .GroupBy(m => m.id.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        return ResolveModifiersForUpgrade(upgrade, modifiersById, modifiers);
    }

    public NodeCatalog NodeCatalog => nodeCatalog;
    public NodeInputCatalog NodeInputCatalog => nodeInputCatalog;
    public NodeInstanceCatalog NodeInstanceCatalog => nodeInstanceCatalog;
    public ResourceCatalog ResourceCatalog => resourceCatalog;
    public UpgradeCatalog UpgradeCatalog => upgradeCatalog;
    public BuffCatalog BuffCatalog => buffCatalog;
    public BuyModeCatalog BuyModeCatalog => buyModeCatalog;
    public GameDefinition Definition => definition;

    private static List<ModifierDefinition> ResolveModifiersForUpgrade(
        UpgradeDefinition upgrade,
        IReadOnlyDictionary<string, ModifierDefinition> modifiersById,
        IReadOnlyList<ModifierDefinition> allModifiers
    )
    {
        var resolved = new List<ModifierDefinition>();

        var effects = upgrade.effects;
        if (effects != null)
        {
            foreach (var effect in effects)
            {
                var modifierId = (effect?.modifierId ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(modifierId))
                    continue;

                if (modifiersById.TryGetValue(modifierId, out var modifier) && modifier != null)
                    resolved.Add(modifier);
            }
        }

        // Fallback for packs where effects[].modifierId drifted but source remains stable.
        if (resolved.Count == 0)
        {
            var upgradeId = (upgrade.id ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(upgradeId))
            {
                for (int i = 0; i < allModifiers.Count; i++)
                {
                    var modifier = allModifiers[i];
                    if (
                        modifier != null
                        && string.Equals(
                            (modifier.source ?? string.Empty).Trim(),
                            upgradeId,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        resolved.Add(modifier);
                    }
                }
            }
        }

        return resolved;
    }

    // TODO: This hasn't really been tested as node inputs are not needed at the moment.
    private void WarnForNodeInputsNotExecuted()
    {
        if (nodeInputCatalog == null || nodeInputCatalog.Count <= 0)
            return;

        Debug.Log(
            $"[NodeInputs] Loaded {nodeInputCatalog.Count} row(s). Node inputs are parsed but not executed in the current vertical slice."
        );

        foreach (var nodeId in nodeInputCatalog.NodeIdsWithInputs)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                continue;

            var normalized = nodeId.Trim();
            if (!warnedNodeInputsByNodeId.Add(normalized))
                continue;

            Debug.LogWarning(
                $"[NodeInputs] Node '{normalized}' has inputs defined but inputs are not executed in the current vertical slice."
            );
        }
    }
}
