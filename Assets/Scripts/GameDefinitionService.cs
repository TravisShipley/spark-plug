using System.Collections.Generic;
using System;
using System.Linq;

public sealed class GameDefinitionService
{
    public const string DefaultPath = "Assets/Data/game_definition.json";
    private readonly string path;
    private GameDefinition definition;
    private NodeCatalog nodeCatalog;
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
        nodeCatalog = new NodeCatalog(definition?.nodes);
        nodeInstanceCatalog = new NodeInstanceCatalog(definition?.nodeInstances);
        NormalizeUpgradeLegacyFields();
        upgradeCatalog = new UpgradeCatalog(definition?.upgrades);
    }

    public IReadOnlyList<NodeDefinition> Nodes => nodeCatalog?.Nodes ?? new List<NodeDefinition>();
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

    public NodeCatalog NodeCatalog => nodeCatalog;
    public NodeInstanceCatalog NodeInstanceCatalog => nodeInstanceCatalog;
    public UpgradeCatalog UpgradeCatalog => upgradeCatalog;
    public UpgradeCatalog Catalog => upgradeCatalog;

    private void NormalizeUpgradeLegacyFields()
    {
        if (definition?.upgrades == null || definition.upgrades.Count == 0)
            return;

        var modifiers = definition.modifiers ?? new List<ModifierEntry>();
        var modifiersById = modifiers
            .Where(m => m != null && !string.IsNullOrWhiteSpace(m.id))
            .GroupBy(m => m.id.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var upgrade in definition.upgrades)
        {
            if (upgrade == null)
                continue;

            var resolved = ResolveModifiersForUpgrade(upgrade, modifiersById, modifiers);
            foreach (var modifier in resolved)
            {
                if (modifier?.scope != null)
                {
                    var scopeKind = (modifier.scope.kind ?? string.Empty).Trim();
                    var scopeNodeId = (modifier.scope.nodeId ?? string.Empty).Trim();
                    if (
                        string.IsNullOrEmpty(upgrade.generatorId)
                        && string.Equals(scopeKind, "node", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(scopeNodeId)
                    )
                    {
                        upgrade.generatorId = scopeNodeId;
                    }
                }

                if (!TryMapModifierToLegacyEffect(modifier, out var effectType, out var value))
                    continue;

                upgrade.effectType = effectType;
                if (value > 0)
                    upgrade.value = value;
                break;
            }
        }
    }

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

    private static bool TryMapModifierToLegacyEffect(
        ModifierEntry modifier,
        out UpgradeEffectType effectType,
        out double value
    )
    {
        effectType = default;
        value = 0;

        if (modifier == null)
            return false;

        var target = (modifier.target ?? string.Empty).Trim();
        var operation = (modifier.operation ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(target))
            return false;

        if (target.StartsWith("nodeSpeedMultiplier", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(operation, "multiply", StringComparison.OrdinalIgnoreCase))
                return false;
            effectType = UpgradeEffectType.NodeSpeedMultiplier;
            value = modifier.value;
            return true;
        }

        if (target.StartsWith("nodeOutput", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(operation, "multiply", StringComparison.OrdinalIgnoreCase))
                return false;
            effectType = UpgradeEffectType.NodeOutput;
            value = modifier.value;
            return true;
        }

        if (string.Equals(target, "automation.policy", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(operation, "set", StringComparison.OrdinalIgnoreCase))
                return false;
            effectType = UpgradeEffectType.AutomationPolicy;
            value = 0;
            return true;
        }

        return false;
    }
}
