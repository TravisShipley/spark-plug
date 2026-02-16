using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class GameDefinitionService
{
    public const string DefaultPath = "Assets/Data/game_definition.json";
    private readonly string path;
    private GameDefinition definition;
    private readonly HashSet<string> warnedNodeInputsByNodeId = new(StringComparer.Ordinal);
    private ResourceCatalog resourceCatalog;
    private NodeCatalog nodeCatalog;
    private NodeInputCatalog nodeInputCatalog;
    private NodeInstanceCatalog nodeInstanceCatalog;
    private UpgradeCatalog upgradeCatalog;

    public GameDefinitionService(string projectRelativePath = "Assets/Data/game_definition.json")
    {
        path = projectRelativePath;
        Reload();
    }

    public void Reload()
    {
        definition = GameDefinitionLoader.LoadFromFile(path);
        resourceCatalog = new ResourceCatalog(definition?.resources);
        nodeCatalog = new NodeCatalog(definition?.nodes);
        nodeInputCatalog = new NodeInputCatalog(definition);
        nodeInstanceCatalog = new NodeInstanceCatalog(definition?.nodeInstances);
        upgradeCatalog = new UpgradeCatalog(definition?.upgrades);
        WarnForNodeInputsNotExecuted();
    }

    public IReadOnlyList<ResourceDefinition> Resources =>
        resourceCatalog?.Resources ?? new List<ResourceDefinition>();
    public IReadOnlyList<NodeDefinition> Nodes => nodeCatalog?.Nodes ?? new List<NodeDefinition>();
    public IReadOnlyList<NodeInputDefinition> NodeInputs =>
        nodeInputCatalog?.NodeInputs ?? new List<NodeInputDefinition>();
    public IReadOnlyList<NodeInstanceDefinition> NodeInstances =>
        nodeInstanceCatalog?.NodeInstances ?? new List<NodeInstanceDefinition>();
    public IReadOnlyList<ModifierEntry> Modifiers =>
        definition?.modifiers ?? new List<ModifierEntry>();
    public IReadOnlyList<UpgradeEntry> Upgrades =>
        upgradeCatalog?.Upgrades ?? new List<UpgradeEntry>();

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

    public bool TryGetUpgrade(string id, out UpgradeEntry entry)
    {
        if (upgradeCatalog == null)
        {
            entry = null;
            return false;
        }

        return upgradeCatalog.TryGet(id, out entry);
    }

    public IReadOnlyList<ModifierEntry> ResolveUpgradeModifiers(string upgradeId)
    {
        var id = (upgradeId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(id))
            return Array.Empty<ModifierEntry>();

        if (!TryGetUpgrade(id, out var upgrade) || upgrade == null)
            return Array.Empty<ModifierEntry>();

        var modifiers = definition?.modifiers ?? new List<ModifierEntry>();
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
    public UpgradeCatalog Catalog => upgradeCatalog;

    private static List<ModifierEntry> ResolveModifiersForUpgrade(
        UpgradeEntry upgrade,
        IReadOnlyDictionary<string, ModifierEntry> modifiersById,
        IReadOnlyList<ModifierEntry> allModifiers
    )
    {
        var resolved = new List<ModifierEntry>();

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
