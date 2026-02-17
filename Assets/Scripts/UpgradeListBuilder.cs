using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

public sealed class UpgradeListBuilder
{
    private readonly UpgradeCatalog upgradeCatalog;
    private readonly UpgradeService upgradeService;
    private readonly GameDefinitionService gameDefinitionService;
    private readonly Dictionary<string, ModifierEntry> modifiersById;

    public UpgradeListBuilder(
        UpgradeCatalog upgradeCatalog,
        UpgradeService upgradeService,
        GameDefinitionService gameDefinitionService
    )
    {
        this.upgradeCatalog = upgradeCatalog ?? throw new ArgumentNullException(nameof(upgradeCatalog));
        this.upgradeService = upgradeService ?? throw new ArgumentNullException(nameof(upgradeService));
        this.gameDefinitionService =
            gameDefinitionService ?? throw new ArgumentNullException(nameof(gameDefinitionService));

        modifiersById = new Dictionary<string, ModifierEntry>(StringComparer.Ordinal);
        var modifiers = gameDefinitionService.Modifiers;
        if (modifiers == null)
            return;

        for (int i = 0; i < modifiers.Count; i++)
        {
            var modifier = modifiers[i];
            var id = (modifier?.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id) || modifiersById.ContainsKey(id))
                continue;

            modifiersById[id] = modifier;
        }
    }

    public IReadOnlyList<UpgradeEntryViewModel> BuildEntries()
    {
        var entries = new List<UpgradeEntryViewModel>();

        var upgrades = upgradeCatalog.Upgrades;
        if (upgrades == null || upgrades.Count == 0)
            return entries;

        for (int i = 0; i < upgrades.Count; i++)
        {
            var upgrade = upgrades[i];
            if (upgrade == null)
                continue;

            var upgradeId = (upgrade.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(upgradeId))
            {
                Debug.LogError("UpgradeListBuilder: Encountered upgrade with empty id.");
                continue;
            }

            bool isValidDefinition = true;

            if (!TryGetPrimaryCost(upgrade, out var costResourceId, out var costAmount))
            {
                isValidDefinition = false;
                Debug.LogError($"UpgradeListBuilder: Upgrade '{upgradeId}' has invalid cost.");
                costResourceId = "currencySoft";
                costAmount = 0.0;
            }

            if (!upgradeService.HasValidModifierReferences(upgradeId, out var validationError))
            {
                isValidDefinition = false;
                Debug.LogError($"UpgradeListBuilder: {validationError}");
            }

            var modifiers = ResolveModifiersFromEffectsStrict(upgrade, ref isValidDefinition);
            if (modifiers.Count == 0)
            {
                isValidDefinition = false;
                Debug.LogError(
                    $"UpgradeListBuilder: Upgrade '{upgradeId}' has no resolvable effects[].modifierId references."
                );
            }

            var summary = BuildUpgradeSummary(modifiers);
            if (!isValidDefinition && string.IsNullOrWhiteSpace(summary))
                summary = "Invalid upgrade configuration";

            entries.Add(
                new UpgradeEntryViewModel(
                    upgrade,
                    upgradeService,
                    summary,
                    costResourceId,
                    costAmount,
                    isValidDefinition
                )
            );
        }

        return entries;
    }

    private List<ModifierEntry> ResolveModifiersFromEffectsStrict(
        UpgradeEntry upgrade,
        ref bool isValidDefinition
    )
    {
        var resolved = new List<ModifierEntry>();
        var upgradeId = (upgrade?.id ?? string.Empty).Trim();
        var effects = upgrade?.effects;

        if (effects == null || effects.Length == 0)
        {
            isValidDefinition = false;
            Debug.LogError($"UpgradeListBuilder: Upgrade '{upgradeId}' has no effects[].");
            return resolved;
        }

        for (int i = 0; i < effects.Length; i++)
        {
            var effect = effects[i];
            if (effect == null)
            {
                isValidDefinition = false;
                Debug.LogError($"UpgradeListBuilder: Upgrade '{upgradeId}' has null effects[{i}].");
                continue;
            }

            var modifierId = (effect.modifierId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(modifierId))
            {
                isValidDefinition = false;
                Debug.LogError(
                    $"UpgradeListBuilder: Upgrade '{upgradeId}' has empty effects[{i}].modifierId."
                );
                continue;
            }

            if (!modifiersById.TryGetValue(modifierId, out var modifier) || modifier == null)
            {
                isValidDefinition = false;
                Debug.LogError(
                    $"UpgradeListBuilder: Upgrade '{upgradeId}' references missing modifierId '{modifierId}'."
                );
                continue;
            }

            resolved.Add(modifier);
        }

        return resolved;
    }

    private string BuildUpgradeSummary(IReadOnlyList<ModifierEntry> modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
            return "modifier-driven";

        var builder = new StringBuilder();
        for (int i = 0; i < modifiers.Count; i++)
        {
            var modifier = modifiers[i];
            if (modifier == null)
                continue;

            if (builder.Length > 0)
                builder.Append(", ");

            builder.Append(BuildModifierSummary(modifier));
        }

        return builder.Length > 0 ? builder.ToString() : "modifier-driven";
    }

    private string BuildModifierSummary(ModifierEntry modifier)
    {
        var target = (modifier.target ?? string.Empty).Trim();
        var scopeKind = (modifier.scope?.kind ?? string.Empty).Trim();
        var scopeNodeId = (modifier.scope?.nodeId ?? string.Empty).Trim();
        var scopeNodeTag = (modifier.scope?.nodeTag ?? string.Empty).Trim();
        var scopeResource = (modifier.scope?.resource ?? string.Empty).Trim();

        string where = "Global";
        if (
            !string.IsNullOrEmpty(scopeNodeId)
            && gameDefinitionService.TryGetNode(scopeNodeId, out var node)
            && node != null
        )
            where = string.IsNullOrWhiteSpace(node.displayName) ? scopeNodeId : node.displayName;
        else if (!string.IsNullOrEmpty(scopeNodeId))
            where = scopeNodeId;
        else if (!string.IsNullOrEmpty(scopeNodeTag))
            where = $"Tag:{scopeNodeTag}";
        else if (string.Equals(scopeKind, "resource", StringComparison.OrdinalIgnoreCase))
            where = $"Resource:{scopeResource}";

        string effect = "modifier";
        if (
            target.StartsWith("nodeSpeedMultiplier", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "node.speedMultiplier", StringComparison.OrdinalIgnoreCase)
        )
            effect = $"speed x{Format.Abbreviated(modifier.value)}";
        else if (
            target.StartsWith("nodeOutput", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "node.outputMultiplier", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("node.outputMultiplier.", StringComparison.OrdinalIgnoreCase)
        )
            effect = $"output x{Format.Abbreviated(modifier.value)}";
        else if (
            string.Equals(target, "automation.policy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "automation.autoCollect", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "automation.autoRestart", StringComparison.OrdinalIgnoreCase)
        )
            effect = "automation enabled";
        else if (target.StartsWith("resourceGain", StringComparison.OrdinalIgnoreCase))
            effect = $"resource gain x{Format.Abbreviated(modifier.value)}";

        return $"{where} {effect}";
    }

    private static bool TryGetPrimaryCost(
        UpgradeEntry upgrade,
        out string costResourceId,
        out double costAmount
    )
    {
        costResourceId = "currencySoft";
        costAmount = 0.0;

        if (upgrade?.cost == null || upgrade.cost.Length == 0 || upgrade.cost[0] == null)
            return false;

        costResourceId = (upgrade.cost[0].resource ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(costResourceId))
            return false;

        return double.TryParse(
            upgrade.cost[0].amount,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out costAmount
        );
    }
}
