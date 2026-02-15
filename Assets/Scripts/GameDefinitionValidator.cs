using System;
using System.Collections.Generic;
using UnityEngine;

public static class GameDefinitionValidator
{
    private static readonly string[] SupportedModifierScopeKinds = { "node" };
    private static readonly string[] SupportedModifierSourceDomains =
    {
        "upgrade",
        "milestone",
        "project",
        "buff",
    };

    public static void Validate(GameDefinition gd)
    {
        if (gd == null)
            throw new ArgumentNullException(nameof(gd));

        var errors = new List<string>();

        // ---- Resources
        var resourceIds = new HashSet<string>(StringComparer.Ordinal);
        if (gd.resources == null || gd.resources.Count == 0)
        {
            errors.Add("resources is empty.");
        }
        else
        {
            for (int i = 0; i < gd.resources.Count; i++)
            {
                var resource = gd.resources[i];
                if (resource == null)
                {
                    errors.Add($"resources[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(resource.id))
                {
                    errors.Add($"resources[{i}].id is empty.");
                    continue;
                }

                if (!resourceIds.Add(resource.id))
                    errors.Add($"Duplicate resource id '{resource.id}'.");
            }
        }

        // ---- Nodes
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < gd.nodes.Count; i++)
        {
            var n = gd.nodes[i];
            if (n == null)
            {
                errors.Add($"nodes[{i}] is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(n.id))
            {
                errors.Add($"nodes[{i}].id is empty.");
                continue;
            }

            if (!nodeIds.Add(n.id))
                errors.Add($"Duplicate node id '{n.id}'.");

            ValidateNodeResourceReferences(n, i, resourceIds, errors);
        }

        // ---- NodeInstances -> Nodes
        var instanceIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < gd.nodeInstances.Count; i++)
        {
            var instance = gd.nodeInstances[i];
            if (instance == null)
            {
                errors.Add($"nodeInstances[{i}] is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(instance.id))
                errors.Add($"nodeInstances[{i}].id is empty.");
            else if (!instanceIds.Add(instance.id))
                errors.Add($"Duplicate nodeInstance id '{instance.id}'.");

            if (string.IsNullOrWhiteSpace(instance.nodeId))
            {
                errors.Add($"nodeInstances[{i}].nodeId is empty.");
            }
            else if (!nodeIds.Contains(instance.nodeId))
            {
                var instanceId = string.IsNullOrWhiteSpace(instance.id) ? $"index {i}" : instance.id;
                errors.Add(
                    $"nodeInstances[{i}] ('{instanceId}') references missing node '{instance.nodeId}'."
                );
            }
        }

        // ---- Modifiers
        var modifierIds = new HashSet<string>(StringComparer.Ordinal);
        if (gd.modifiers != null)
        {
            for (int i = 0; i < gd.modifiers.Count; i++)
            {
                var m = gd.modifiers[i];
                if (m == null)
                {
                    errors.Add($"modifiers[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(m.id))
                    errors.Add($"modifiers[{i}].id is empty.");
                else if (!modifierIds.Add(m.id))
                    errors.Add($"Duplicate modifier id '{m.id}'.");

                ValidateModifierVerticalSlice(m, i, nodeIds, errors);
            }
        }

        // ---- Upgrades basic integrity + effects references
        if (gd.upgrades != null)
        {
            var upgradeIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < gd.upgrades.Count; i++)
            {
                var u = gd.upgrades[i];
                if (u == null)
                {
                    errors.Add($"upgrades[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(u.id))
                    errors.Add($"upgrades[{i}].id is empty.");
                else if (!upgradeIds.Add(u.id))
                    errors.Add($"Duplicate upgrade id '{u.id}'.");

                ValidateUpgradeEffectsReferences(u, i, modifierIds, errors);
                ValidateUpgradeCostResources(u, i, resourceIds, errors);
            }

            // Optional reference check: when modifier.source is set, it should point to an existing upgrade id.
            if (gd.modifiers != null)
            {
                for (int i = 0; i < gd.modifiers.Count; i++)
                {
                    var modifier = gd.modifiers[i];
                    if (modifier == null)
                        continue;

                    var source = (modifier.source ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(source))
                    {
                        ValidateModifierSource(source, modifier, i, upgradeIds, errors);
                    }
                }
            }
        }

        if (errors.Count > 0)
        {
            for (int i = 0; i < errors.Count; i++)
                Debug.LogError($"GameDefinition validation error: {errors[i]}");

            throw new InvalidOperationException(
                $"GameDefinition validation failed with {errors.Count} error(s). See console for details."
            );
        }
    }

    private static void ValidateUpgradeEffectsReferences(
        UpgradeEntry upgrade,
        int upgradeIndex,
        HashSet<string> modifierIds,
        List<string> errors
    )
    {
        if (upgrade.effects == null || upgrade.effects.Length == 0)
            return;

        for (int i = 0; i < upgrade.effects.Length; i++)
        {
            var effect = upgrade.effects[i];
            if (effect == null)
            {
                errors.Add($"upgrades[{upgradeIndex}] ('{upgrade.id}') effects[{i}] is null.");
                continue;
            }

            var modifierId = (effect.modifierId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(modifierId))
            {
                errors.Add($"upgrades[{upgradeIndex}] ('{upgrade.id}') effects[{i}].modifierId is empty.");
                continue;
            }

            if (!modifierIds.Contains(modifierId))
            {
                errors.Add(
                    $"upgrades[{upgradeIndex}] ('{upgrade.id}') effects[{i}].modifierId '{modifierId}' references missing modifiers.id."
                );
            }
        }
    }

    private static void ValidateNodeResourceReferences(
        NodeDefinition node,
        int nodeIndex,
        HashSet<string> resourceIds,
        List<string> errors
    )
    {
        var levelResource = (node?.leveling?.levelResource ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(levelResource))
        {
            errors.Add($"nodes[{nodeIndex}] ('{node?.id}') leveling.levelResource is empty.");
        }
        else if (!resourceIds.Contains(levelResource))
        {
            errors.Add(
                $"nodes[{nodeIndex}] ('{node?.id}') leveling.levelResource '{levelResource}' references missing resources.id."
            );
        }

        if (node?.outputs == null)
            return;

        for (int i = 0; i < node.outputs.Count; i++)
        {
            var output = node.outputs[i];
            if (output == null)
            {
                errors.Add($"nodes[{nodeIndex}] ('{node?.id}') outputs[{i}] is null.");
                continue;
            }

            var outputResource = (output.resource ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(outputResource))
            {
                errors.Add($"nodes[{nodeIndex}] ('{node?.id}') outputs[{i}].resource is empty.");
            }
            else if (!resourceIds.Contains(outputResource))
            {
                errors.Add(
                    $"nodes[{nodeIndex}] ('{node?.id}') outputs[{i}].resource '{outputResource}' references missing resources.id."
                );
            }
        }
    }

    private static void ValidateUpgradeCostResources(
        UpgradeEntry upgrade,
        int upgradeIndex,
        HashSet<string> resourceIds,
        List<string> errors
    )
    {
        if (upgrade?.cost == null || upgrade.cost.Length == 0)
            return;

        for (int i = 0; i < upgrade.cost.Length; i++)
        {
            var cost = upgrade.cost[i];
            if (cost == null)
            {
                errors.Add($"upgrades[{upgradeIndex}] ('{upgrade.id}') cost[{i}] is null.");
                continue;
            }

            var resourceId = (cost.resource ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(resourceId))
            {
                errors.Add($"upgrades[{upgradeIndex}] ('{upgrade.id}') cost[{i}].resource is empty.");
            }
            else if (!resourceIds.Contains(resourceId))
            {
                errors.Add(
                    $"upgrades[{upgradeIndex}] ('{upgrade.id}') cost[{i}].resource '{resourceId}' references missing resources.id."
                );
            }

        }
    }

    private static void ValidateModifierVerticalSlice(
        ModifierEntry modifier,
        int modifierIndex,
        HashSet<string> nodeIds,
        List<string> errors
    )
    {
        var scopeKind = (modifier.scope?.kind ?? string.Empty).Trim();
        var scopeNodeId = (modifier.scope?.nodeId ?? string.Empty).Trim();
        var operation = (modifier.operation ?? string.Empty).Trim();
        var target = (modifier.target ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(scopeKind))
        {
            errors.Add($"modifiers[{modifierIndex}] ('{modifier.id}') scope.kind is empty.");
        }
        else if (!ContainsIgnoreCase(SupportedModifierScopeKinds, scopeKind))
        {
            errors.Add(
                $"modifiers[{modifierIndex}] ('{modifier.id}') scope.kind '{scopeKind}' is unsupported for current vertical slice."
            );
        }

        if (string.IsNullOrEmpty(scopeNodeId))
        {
            errors.Add($"modifiers[{modifierIndex}] ('{modifier.id}') scope.nodeId is empty.");
        }
        else if (!nodeIds.Contains(scopeNodeId))
        {
            errors.Add(
                $"modifiers[{modifierIndex}] ('{modifier.id}') scope.nodeId '{scopeNodeId}' references missing nodes.id."
            );
        }

        if (string.IsNullOrEmpty(target))
        {
            errors.Add($"modifiers[{modifierIndex}] ('{modifier.id}') target is empty.");
            return;
        }

        if (
            !target.StartsWith("nodeSpeedMultiplier", StringComparison.OrdinalIgnoreCase)
            && !target.StartsWith("nodeOutput", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(target, "automation.policy", StringComparison.OrdinalIgnoreCase)
        )
        {
            errors.Add(
                $"modifiers[{modifierIndex}] ('{modifier.id}') target '{target}' is unsupported for current vertical slice."
            );
            return;
        }

        if (
            target.StartsWith("nodeSpeedMultiplier", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("nodeOutput", StringComparison.OrdinalIgnoreCase)
        )
        {
            if (!string.Equals(operation, "multiply", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"modifiers[{modifierIndex}] ('{modifier.id}') operation '{operation}' is unsupported for target '{target}'. Expected 'multiply'."
                );
            }

            return;
        }

        if (string.Equals(target, "automation.policy", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(operation, "set", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"modifiers[{modifierIndex}] ('{modifier.id}') operation '{operation}' is unsupported for target '{target}'. Expected 'set'."
                );
            }
        }
    }

    private static bool ContainsIgnoreCase(IEnumerable<string> values, string value)
    {
        foreach (var v in values)
        {
            if (string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void ValidateModifierSource(
        string source,
        ModifierEntry modifier,
        int modifierIndex,
        HashSet<string> upgradeIds,
        List<string> errors
    )
    {
        var dot = source.IndexOf('.');
        if (dot <= 0 || dot == source.Length - 1)
        {
            errors.Add(
                $"modifiers[{modifierIndex}] ('{modifier.id}') source '{source}' is invalid. "
                    + "Expected a domain-prefixed id like 'upgrade.*', 'milestone.*', 'project.*', or 'buff.*'."
            );
            return;
        }

        var domain = source.Substring(0, dot).Trim();
        if (!ContainsIgnoreCase(SupportedModifierSourceDomains, domain))
        {
            errors.Add(
                $"modifiers[{modifierIndex}] ('{modifier.id}') source domain '{domain}' is unsupported. "
                    + "Allowed: upgrade, milestone, project, buff."
            );
            return;
        }

        // For upgrades we can validate exact existence in current content.
        if (
            string.Equals(domain, "upgrade", StringComparison.OrdinalIgnoreCase)
            && !upgradeIds.Contains(source)
        )
        {
            errors.Add(
                $"modifiers[{modifierIndex}] ('{modifier.id}') source '{source}' does not match any upgrades.id."
            );
        }
    }
}
