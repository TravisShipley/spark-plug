using System;
using System.Collections.Generic;
using UnityEngine;

public static class GameDefinitionValidator
{
    private static readonly string[] SupportedModifierScopeKinds =
    {
        "global",
        "zone",
        "node",
        "nodeTag",
        "resource",
    };
    private static readonly string[] SupportedModifierSourceDomains =
    {
        "upgrade",
        "milestone",
        "project",
        "buff",
    };
    private static readonly string[] SupportedBuffStackingValues =
    {
        "none",
        "refresh",
        "extend",
        "stack",
    };
    private static readonly string[] SupportedTriggerEvents =
    {
        "node.cycleComplete",
        "node.collect",
        "node.start",
        "node.levelPurchased",
        "resource.gained",
        "offline.claim",
        "ad.rewarded",
        "session.tick",
        "prestige.reset",
        "upgrade.purchased",
        "milestone.fired",
    };

    private static readonly string[] SupportedTriggerConditionTypes = { "milestoneIdEquals" };

    private static readonly string[] SupportedTriggerActionTypes = { "rollRewardPool", "timeWarp" };

    private static readonly string[] SupportedRewardActionTypes = { "grantResource", "timeWarp" };
    private static readonly string[] SupportedBuyModeKinds =
    {
        "fixed",
        "nextMilestone",
        "maxAffordable",
    };
    private static readonly string[] SupportedStateVarKinds = { "quantity", "counter", "timer" };

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
        if (gd.nodes == null)
            errors.Add("nodes is null. Use [] when no nodes are defined.");
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var nodeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; gd.nodes != null && i < gd.nodes.Count; i++)
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

            if (n.tags != null)
            {
                for (int t = 0; t < n.tags.Length; t++)
                {
                    var tag = (n.tags[t] ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(tag))
                        nodeTags.Add(tag);
                }
            }

            ValidateNodeResourceReferences(n, i, resourceIds, errors);
            ValidateNodeLocalInputsReferences(n, i, resourceIds, errors);
        }

        ValidateTopLevelNodeInputsReferences(gd.nodeInputs, nodeIds, resourceIds, errors);

        var stateVarIds = ValidateStateVars(gd.stateVars, errors);
        ValidateNodeStateCapacities(gd.nodeStateCapacities, nodeIds, stateVarIds, errors);

        // ---- NodeInstances -> Nodes
        if (gd.nodeInstances == null)
            errors.Add("nodeInstances is null. Use [] when no nodeInstances are defined.");
        var instanceIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; gd.nodeInstances != null && i < gd.nodeInstances.Count; i++)
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
                var instanceId = string.IsNullOrWhiteSpace(instance.id)
                    ? $"index {i}"
                    : instance.id;
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

                ValidateModifier(m, i, nodeIds, nodeTags, resourceIds, errors);
            }
        }

        // ---- Upgrades basic integrity + effects references
        var upgradeIds = new HashSet<string>(StringComparer.Ordinal);
        if (gd.upgrades != null)
        {
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
        }

        if (gd.milestones == null)
            errors.Add("milestones is null. Use [] when no milestones are defined.");
        var milestoneIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; gd.milestones != null && i < gd.milestones.Count; i++)
        {
            var milestone = gd.milestones[i];
            if (milestone == null)
            {
                errors.Add($"milestones[{i}] is null.");
                continue;
            }

            var milestoneId = (milestone.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(milestoneId))
            {
                errors.Add($"milestones[{i}].id is empty.");
                continue;
            }

            if (!milestoneIds.Add(milestoneId))
                errors.Add($"Duplicate milestone id '{milestoneId}'.");
        }

        var buffIds = new HashSet<string>(StringComparer.Ordinal);
        ValidateBuffEntries(gd.buffs, modifierIds, buffIds, errors);

        var rewardPoolIds = ValidateRewardPools(gd.rewardPools, resourceIds, errors);
        ValidateTriggers(gd.triggers, nodeIds, milestoneIds, rewardPoolIds, errors);
        ValidateBuyModes(gd.buyModes, errors);
        ValidateFormulaPaths(gd, resourceIds, errors);
        ValidatePrestigeConfiguration(gd, resourceIds, errors);

        // Optional reference check: when modifier.source is set, it should point to a known content id.
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
                    ValidateModifierSource(source, modifier, i, upgradeIds, buffIds, errors);
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
        UpgradeDefinition upgrade,
        int upgradeIndex,
        HashSet<string> modifierIds,
        List<string> errors
    )
    {
        if (upgrade.effects == null || upgrade.effects.Length == 0)
        {
            errors.Add(
                $"Upgrade '{upgrade.id}' has no effects. Upgrades must define effects[].modifierId."
            );
            return;
        }

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
                errors.Add($"Upgrade '{upgrade.id}' has empty effects[{i}].modifierId.");
                continue;
            }

            if (!modifierIds.Contains(modifierId))
            {
                errors.Add(
                    $"Upgrade '{upgrade.id}' references missing modifierId '{modifierId}'. Fix the sheet."
                );
            }
        }
    }

    private static HashSet<string> ValidateRewardPools(
        IReadOnlyList<RewardPoolDefinition> rewardPools,
        HashSet<string> resourceIds,
        List<string> errors
    )
    {
        var rewardPoolIds = new HashSet<string>(StringComparer.Ordinal);
        if (rewardPools == null)
        {
            errors.Add("rewardPools is null. Use [] when no reward pools are defined.");
            return rewardPoolIds;
        }

        for (int i = 0; i < rewardPools.Count; i++)
        {
            var rewardPool = rewardPools[i];
            if (rewardPool == null)
            {
                errors.Add($"rewardPools[{i}] is null.");
                continue;
            }

            var rewardPoolId = (rewardPool.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(rewardPoolId))
            {
                errors.Add($"rewardPools[{i}].id is empty.");
                continue;
            }

            if (!rewardPoolIds.Add(rewardPoolId))
                errors.Add($"Duplicate rewardPool id '{rewardPoolId}'.");

            if (rewardPool.rewards == null)
            {
                errors.Add($"rewardPools[{i}] ('{rewardPoolId}') rewards is null.");
                continue;
            }

            for (int r = 0; r < rewardPool.rewards.Length; r++)
            {
                var reward = rewardPool.rewards[r];
                if (reward == null)
                {
                    errors.Add($"rewardPools[{i}] ('{rewardPoolId}') rewards[{r}] is null.");
                    continue;
                }

                if (reward.weight <= 0f)
                {
                    errors.Add(
                        $"rewardPools[{i}] ('{rewardPoolId}') rewards[{r}].weight must be > 0."
                    );
                }

                var action = reward.action;
                if (action == null)
                {
                    errors.Add($"rewardPools[{i}] ('{rewardPoolId}') rewards[{r}].action is null.");
                    continue;
                }

                var actionType = (action.type ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(actionType))
                {
                    errors.Add(
                        $"rewardPools[{i}] ('{rewardPoolId}') rewards[{r}].action.type is empty."
                    );
                    continue;
                }

                if (!ContainsIgnoreCase(SupportedRewardActionTypes, actionType))
                {
                    errors.Add(
                        $"rewardPools[{i}] ('{rewardPoolId}') rewards[{r}].action.type '{actionType}' is unsupported."
                    );
                    continue;
                }

                if (string.Equals(actionType, "grantResource", StringComparison.OrdinalIgnoreCase))
                {
                    var resourceId = (action.resourceId ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(resourceId))
                    {
                        errors.Add(
                            $"rewardPools[{i}] ('{rewardPoolId}') rewards[{r}].action.resourceId is empty."
                        );
                    }
                    else if (!resourceIds.Contains(resourceId))
                    {
                        errors.Add(
                            $"rewardPools[{i}] ('{rewardPoolId}') rewards[{r}].action.resourceId '{resourceId}' references missing resources.id."
                        );
                    }

                    if (
                        double.IsNaN(action.amount)
                        || double.IsInfinity(action.amount)
                        || action.amount <= 0d
                    )
                    {
                        errors.Add(
                            $"rewardPools[{i}] ('{rewardPoolId}') rewards[{r}].action.amount must be > 0."
                        );
                    }
                }
                else if (string.Equals(actionType, "timeWarp", StringComparison.OrdinalIgnoreCase))
                {
                    if (
                        double.IsNaN(action.durationSeconds)
                        || double.IsInfinity(action.durationSeconds)
                        || action.durationSeconds <= 0d
                    )
                    {
                        errors.Add(
                            $"rewardPools[{i}] ('{rewardPoolId}') rewards[{r}].action.durationSeconds must be > 0."
                        );
                    }
                }
            }
        }

        return rewardPoolIds;
    }

    private static void ValidateTriggers(
        IReadOnlyList<TriggerDefinition> triggers,
        HashSet<string> nodeIds,
        HashSet<string> milestoneIds,
        HashSet<string> rewardPoolIds,
        List<string> errors
    )
    {
        if (triggers == null)
        {
            errors.Add("triggers is null. Use [] when no triggers are defined.");
            return;
        }

        var triggerIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < triggers.Count; i++)
        {
            var trigger = triggers[i];
            if (trigger == null)
            {
                errors.Add($"triggers[{i}] is null.");
                continue;
            }

            var triggerId = (trigger.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(triggerId))
            {
                errors.Add($"triggers[{i}].id is empty.");
            }
            else if (!triggerIds.Add(triggerId))
            {
                errors.Add($"Duplicate trigger id '{triggerId}'.");
            }

            var eventType = ResolveTriggerEventType(trigger, i, errors);
            if (
                !string.IsNullOrEmpty(eventType)
                && !ContainsIgnoreCase(SupportedTriggerEvents, eventType)
            )
            {
                errors.Add($"triggers[{i}] ('{triggerId}') event '{eventType}' is unsupported.");
            }

            var scopeNodeId = (trigger.scope?.nodeId ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(scopeNodeId) && !nodeIds.Contains(scopeNodeId))
            {
                errors.Add(
                    $"triggers[{i}] ('{triggerId}') scope.nodeId '{scopeNodeId}' references missing nodes.id."
                );
            }

            if (trigger.conditions == null)
            {
                errors.Add(
                    $"triggers[{i}] ('{triggerId}') conditions is null. Use [] when no conditions are needed."
                );
            }
            else
            {
                for (int c = 0; c < trigger.conditions.Length; c++)
                {
                    var condition = trigger.conditions[c];
                    if (condition == null)
                    {
                        errors.Add($"triggers[{i}] ('{triggerId}') conditions[{c}] is null.");
                        continue;
                    }

                    var conditionType = (condition.type ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(conditionType))
                    {
                        errors.Add($"triggers[{i}] ('{triggerId}') conditions[{c}].type is empty.");
                        continue;
                    }

                    if (!ContainsIgnoreCase(SupportedTriggerConditionTypes, conditionType))
                    {
                        errors.Add(
                            $"triggers[{i}] ('{triggerId}') conditions[{c}].type '{conditionType}' is unsupported."
                        );
                        continue;
                    }

                    if (
                        string.Equals(
                            conditionType,
                            "milestoneIdEquals",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        if (condition.args == null)
                        {
                            errors.Add(
                                $"triggers[{i}] ('{triggerId}') conditions[{c}].args is required for '{conditionType}'."
                            );
                            continue;
                        }

                        var milestoneId = (condition.args.milestoneId ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(milestoneId))
                        {
                            errors.Add(
                                $"triggers[{i}] ('{triggerId}') conditions[{c}].args.milestoneId is empty."
                            );
                        }
                        else if (!milestoneIds.Contains(milestoneId))
                        {
                            errors.Add(
                                $"triggers[{i}] ('{triggerId}') conditions[{c}].args.milestoneId '{milestoneId}' references missing milestones.id."
                            );
                        }
                    }
                }
            }

            if (trigger.actions == null)
            {
                errors.Add($"triggers[{i}] ('{triggerId}') actions is null.");
                continue;
            }

            if (trigger.actions.Length == 0)
            {
                errors.Add(
                    $"triggers[{i}] ('{triggerId}') actions must contain at least one item."
                );
                continue;
            }

            for (int a = 0; a < trigger.actions.Length; a++)
            {
                var action = trigger.actions[a];
                if (action == null)
                {
                    errors.Add($"triggers[{i}] ('{triggerId}') actions[{a}] is null.");
                    continue;
                }

                var actionType = (action.type ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(actionType))
                {
                    errors.Add($"triggers[{i}] ('{triggerId}') actions[{a}].type is empty.");
                    continue;
                }

                if (!ContainsIgnoreCase(SupportedTriggerActionTypes, actionType))
                {
                    errors.Add(
                        $"triggers[{i}] ('{triggerId}') actions[{a}].type '{actionType}' is unsupported."
                    );
                    continue;
                }

                if (string.Equals(actionType, "rollRewardPool", StringComparison.OrdinalIgnoreCase))
                {
                    var rewardPoolId = (action.rewardPoolId ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(rewardPoolId))
                    {
                        errors.Add(
                            $"triggers[{i}] ('{triggerId}') actions[{a}].rewardPoolId is empty."
                        );
                        continue;
                    }

                    if (!rewardPoolIds.Contains(rewardPoolId))
                    {
                        errors.Add(
                            $"triggers[{i}] ('{triggerId}') actions[{a}].rewardPoolId '{rewardPoolId}' references missing rewardPools.id."
                        );
                    }
                }
                else if (string.Equals(actionType, "timeWarp", StringComparison.OrdinalIgnoreCase))
                {
                    if (
                        double.IsNaN(action.durationSeconds)
                        || double.IsInfinity(action.durationSeconds)
                        || action.durationSeconds <= 0d
                    )
                    {
                        errors.Add(
                            $"triggers[{i}] ('{triggerId}') actions[{a}].durationSeconds must be > 0."
                        );
                    }
                }
            }
        }
    }

    private static string ResolveTriggerEventType(
        TriggerDefinition trigger,
        int triggerIndex,
        List<string> errors
    )
    {
        var eventType = (trigger?.eventType ?? string.Empty).Trim();
        var eventAlias = (trigger?.@event ?? string.Empty).Trim();

        if (
            !string.IsNullOrEmpty(eventType)
            && !string.IsNullOrEmpty(eventAlias)
            && !string.Equals(eventType, eventAlias, StringComparison.Ordinal)
        )
        {
            var triggerId = (trigger?.id ?? string.Empty).Trim();
            errors.Add(
                $"triggers[{triggerIndex}] ('{triggerId}') has mismatched eventType '{eventType}' and event '{eventAlias}'."
            );
        }

        var resolved = !string.IsNullOrEmpty(eventType) ? eventType : eventAlias;
        if (string.IsNullOrEmpty(resolved))
        {
            var triggerId = (trigger?.id ?? string.Empty).Trim();
            errors.Add($"triggers[{triggerIndex}] ('{triggerId}') event is empty.");
        }

        return resolved;
    }

    private static void ValidateBuffEntries(
        IReadOnlyList<BuffDefinition> buffs,
        HashSet<string> modifierIds,
        HashSet<string> buffIds,
        List<string> errors
    )
    {
        if (buffs == null)
            return;

        for (int i = 0; i < buffs.Count; i++)
        {
            var buff = buffs[i];
            if (buff == null)
            {
                errors.Add($"buffs[{i}] is null.");
                continue;
            }

            var buffId = (buff.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(buffId))
            {
                errors.Add($"buffs[{i}].id is empty.");
                continue;
            }

            if (!buffIds.Add(buffId))
                errors.Add($"Duplicate buff id '{buffId}'.");

            if (buff.durationSeconds <= 0)
            {
                errors.Add(
                    $"buffs[{i}] ('{buffId}') durationSeconds must be > 0. Found '{buff.durationSeconds}'."
                );
            }

            var stacking = (buff.stacking ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(stacking))
                stacking = "none";

            if (!ContainsIgnoreCase(SupportedBuffStackingValues, stacking))
            {
                errors.Add(
                    $"buffs[{i}] ('{buffId}') stacking '{buff.stacking}' is unsupported. Expected 'none|refresh|extend|stack'."
                );
            }

            if (buff.effects == null || buff.effects.Length == 0)
            {
                errors.Add($"buffs[{i}] ('{buffId}') has no effects[].modifierId entries.");
                continue;
            }

            for (int e = 0; e < buff.effects.Length; e++)
            {
                var effect = buff.effects[e];
                if (effect == null)
                {
                    errors.Add($"buffs[{i}] ('{buffId}') effects[{e}] is null.");
                    continue;
                }

                var modifierId = (effect.modifierId ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(modifierId))
                {
                    errors.Add($"buffs[{i}] ('{buffId}') effects[{e}].modifierId is empty.");
                    continue;
                }

                if (!modifierIds.Contains(modifierId))
                {
                    errors.Add(
                        $"buffs[{i}] ('{buffId}') references missing modifierId '{modifierId}'."
                    );
                }
            }
        }
    }

    private static void ValidateBuyModes(
        IReadOnlyList<BuyModeDefinition> buyModes,
        List<string> errors
    )
    {
        if (buyModes == null || buyModes.Count == 0)
            return;

        var buyModeIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < buyModes.Count; i++)
        {
            var buyMode = buyModes[i];
            if (buyMode == null)
            {
                errors.Add($"buyModes[{i}] is null.");
                continue;
            }

            var id = (buyMode.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
            {
                errors.Add($"buyModes[{i}].id is empty.");
            }
            else if (!buyModeIds.Add(id))
            {
                errors.Add($"Duplicate buyMode id '{id}'.");
            }

            var displayName = (buyMode.displayName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(displayName))
                errors.Add($"buyModes[{i}] ('{id}') displayName is empty.");

            var kind = (buyMode.kind ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(kind))
            {
                errors.Add($"buyModes[{i}] ('{id}') kind is empty.");
                continue;
            }

            if (!ContainsIgnoreCase(SupportedBuyModeKinds, kind))
            {
                errors.Add(
                    $"buyModes[{i}] ('{id}') kind '{kind}' is unsupported. Allowed: fixed, nextMilestone, maxAffordable."
                );
                continue;
            }

            if (
                string.Equals(kind, "fixed", StringComparison.OrdinalIgnoreCase)
                && buyMode.fixedCount < 1
            )
            {
                errors.Add($"buyModes[{i}] ('{id}') fixedCount must be >= 1 for fixed mode.");
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
        UpgradeDefinition upgrade,
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
                errors.Add(
                    $"upgrades[{upgradeIndex}] ('{upgrade.id}') cost[{i}].resource is empty."
                );
            }
            else if (!resourceIds.Contains(resourceId))
            {
                errors.Add(
                    $"upgrades[{upgradeIndex}] ('{upgrade.id}') cost[{i}].resource '{resourceId}' references missing resources.id."
                );
            }
        }
    }

    private static void ValidateNodeLocalInputsReferences(
        NodeDefinition node,
        int nodeIndex,
        HashSet<string> resourceIds,
        List<string> errors
    )
    {
        if (node?.inputs == null)
            return;

        for (int i = 0; i < node.inputs.Count; i++)
        {
            var input = node.inputs[i];
            if (input == null)
            {
                errors.Add($"nodes[{nodeIndex}] ('{node?.id}') inputs[{i}] is null.");
                continue;
            }

            var inputResource = (input.resource ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(inputResource))
            {
                errors.Add($"nodes[{nodeIndex}] ('{node?.id}') inputs[{i}].resource is empty.");
            }
            else if (!resourceIds.Contains(inputResource))
            {
                errors.Add(
                    $"nodes[{nodeIndex}] ('{node?.id}') inputs[{i}].resource '{inputResource}' references missing resources.id."
                );
            }
        }
    }

    private static void ValidateTopLevelNodeInputsReferences(
        IReadOnlyList<NodeInputDefinition> nodeInputs,
        HashSet<string> nodeIds,
        HashSet<string> resourceIds,
        List<string> errors
    )
    {
        if (nodeInputs == null)
            return;

        for (int i = 0; i < nodeInputs.Count; i++)
        {
            var input = nodeInputs[i];
            if (input == null)
            {
                errors.Add($"nodeInputs[{i}] is null.");
                continue;
            }

            var nodeId = (input.nodeId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(nodeId))
            {
                errors.Add($"nodeInputs[{i}].nodeId is empty.");
            }
            else if (!nodeIds.Contains(nodeId))
            {
                errors.Add($"nodeInputs[{i}].nodeId '{nodeId}' references missing nodes.id.");
            }

            var inputResource = (input.resource ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(inputResource))
            {
                errors.Add($"nodeInputs[{i}].resource is empty.");
            }
            else if (!resourceIds.Contains(inputResource))
            {
                errors.Add(
                    $"nodeInputs[{i}].resource '{inputResource}' references missing resources.id."
                );
            }
        }
    }

    private static HashSet<string> ValidateStateVars(
        IReadOnlyList<StateVarDefinition> stateVars,
        List<string> errors
    )
    {
        var stateVarIds = new HashSet<string>(StringComparer.Ordinal);
        if (stateVars == null)
            return stateVarIds;

        for (int i = 0; i < stateVars.Count; i++)
        {
            var stateVar = stateVars[i];
            if (stateVar == null)
            {
                errors.Add($"stateVars[{i}] is null.");
                continue;
            }

            var id = NormalizeId(stateVar.id);
            if (string.IsNullOrEmpty(id))
            {
                errors.Add($"stateVars[{i}].id is empty.");
                continue;
            }

            if (!stateVarIds.Add(id))
                errors.Add($"Duplicate stateVars id '{id}'.");

            var kind = NormalizeId(stateVar.kind);
            if (string.IsNullOrEmpty(kind))
            {
                errors.Add($"stateVars[{i}] ('{id}') kind is empty.");
            }
            else if (!ContainsIgnoreCase(SupportedStateVarKinds, kind))
            {
                errors.Add(
                    $"stateVars[{i}] ('{id}') kind '{kind}' is unsupported. Allowed: quantity, counter, timer."
                );
            }

            if (double.IsNaN(stateVar.defaultValue) || double.IsInfinity(stateVar.defaultValue))
            {
                errors.Add(
                    $"stateVars[{i}] ('{id}') defaultValue must be a parseable finite number."
                );
            }
        }

        return stateVarIds;
    }

    private static void ValidateNodeStateCapacities(
        IReadOnlyList<NodeStateCapacityDefinition> nodeStateCapacities,
        HashSet<string> nodeIds,
        HashSet<string> stateVarIds,
        List<string> errors
    )
    {
        if (nodeStateCapacities == null)
            return;

        for (int i = 0; i < nodeStateCapacities.Count; i++)
        {
            var capacity = nodeStateCapacities[i];
            if (capacity == null)
            {
                errors.Add($"nodeStateCapacities[{i}] is null.");
                continue;
            }

            var nodeId = NormalizeId(capacity.nodeId);
            if (string.IsNullOrEmpty(nodeId))
            {
                errors.Add($"nodeStateCapacities[{i}].nodeId is empty.");
            }
            else if (!nodeIds.Contains(nodeId))
            {
                errors.Add(
                    $"nodeStateCapacities[{i}].nodeId '{nodeId}' references missing nodes.id."
                );
            }

            var varId = NormalizeId(capacity.varId);
            if (string.IsNullOrEmpty(varId))
            {
                errors.Add($"nodeStateCapacities[{i}].varId is empty.");
            }
            else if (!stateVarIds.Contains(varId))
            {
                errors.Add(
                    $"nodeStateCapacities[{i}].varId '{varId}' references missing stateVars.id."
                );
            }

            if (double.IsNaN(capacity.baseCapacity) || double.IsInfinity(capacity.baseCapacity))
            {
                errors.Add(
                    $"nodeStateCapacities[{i}] ('{nodeId}' -> '{varId}') baseCapacity must be a parseable finite number."
                );
            }
        }
    }

    private static void ValidateModifier(
        ModifierDefinition modifier,
        int modifierIndex,
        HashSet<string> nodeIds,
        HashSet<string> nodeTags,
        HashSet<string> resourceIds,
        List<string> errors
    )
    {
        var scopeKind = (modifier.scope?.kind ?? string.Empty).Trim();
        var scopeNodeId = (modifier.scope?.nodeId ?? string.Empty).Trim();
        var scopeNodeTag = (modifier.scope?.nodeTag ?? string.Empty).Trim();
        var scopeResourceId = (modifier.scope?.resource ?? string.Empty).Trim();
        var target = (modifier.target ?? string.Empty).Trim();
        ParameterizedPathParser.ParsedPath parsedTarget;
        var hasParameterizedTarget = ParameterizedPathParser.TryParseModifierParameterizedPath(
            target,
            out parsedTarget
        );

        if (hasParameterizedTarget)
        {
            var canonical = parsedTarget.CanonicalPath;
            if (!string.Equals(target, canonical, StringComparison.Ordinal))
            {
#if UNITY_EDITOR
                Debug.LogWarning(
                    $"GameDefinitionValidator: modifier '{modifier.id}' target '{target}' should be '{canonical}'. Prefer bracket form."
                );
#endif
                target = canonical;
            }
        }

        if (string.IsNullOrEmpty(scopeKind))
        {
            errors.Add($"modifiers[{modifierIndex}] ('{modifier.id}') scope.kind is empty.");
        }
        else if (!ContainsIgnoreCase(SupportedModifierScopeKinds, scopeKind))
        {
            errors.Add(
                $"modifiers[{modifierIndex}] ('{modifier.id}') scope.kind '{scopeKind}' is unsupported."
            );
        }

        if (string.Equals(scopeKind, "node", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(scopeNodeId) && string.IsNullOrEmpty(scopeNodeTag))
            {
                errors.Add(
                    $"modifiers[{modifierIndex}] ('{modifier.id}') node scope requires scope.nodeId or scope.nodeTag."
                );
            }
            else if (!string.IsNullOrEmpty(scopeNodeId) && !nodeIds.Contains(scopeNodeId))
            {
                errors.Add(
                    $"modifiers[{modifierIndex}] ('{modifier.id}') scope.nodeId '{scopeNodeId}' references missing nodes.id."
                );
            }
            else if (!string.IsNullOrEmpty(scopeNodeTag) && !nodeTags.Contains(scopeNodeTag))
            {
                errors.Add(
                    $"modifiers[{modifierIndex}] ('{modifier.id}') scope.nodeTag '{scopeNodeTag}' references missing node tags."
                );
            }
        }

        if (string.Equals(scopeKind, "nodeTag", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(scopeNodeTag))
            {
                errors.Add($"modifiers[{modifierIndex}] ('{modifier.id}') scope.nodeTag is empty.");
            }
            else if (!nodeTags.Contains(scopeNodeTag))
            {
                errors.Add(
                    $"modifiers[{modifierIndex}] ('{modifier.id}') scope.nodeTag '{scopeNodeTag}' references missing node tags."
                );
            }
        }

        if (string.Equals(scopeKind, "resource", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(scopeResourceId))
            {
                errors.Add(
                    $"modifiers[{modifierIndex}] ('{modifier.id}') scope.resource is empty."
                );
            }
            else if (!resourceIds.Contains(scopeResourceId))
            {
                errors.Add(
                    $"modifiers[{modifierIndex}] ('{modifier.id}') scope.resource '{scopeResourceId}' references missing resources.id."
                );
            }
        }

        if (string.IsNullOrEmpty(target))
        {
            errors.Add($"modifiers[{modifierIndex}] ('{modifier.id}') target is empty.");
            return;
        }

        var reportedMissingResourceIds = new HashSet<string>(StringComparer.Ordinal);

        if (
            hasParameterizedTarget
            && !string.IsNullOrEmpty(parsedTarget.ParameterId)
            && !resourceIds.Contains(parsedTarget.ParameterId)
            && reportedMissingResourceIds.Add(parsedTarget.ParameterId)
        )
        {
            errors.Add(
                $"modifiers[{modifierIndex}] ('{modifier.id}') target resource '{parsedTarget.ParameterId}' references missing resources.id."
            );
        }

        if (
            target.StartsWith("resourceGain[", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("nodeOutput[", StringComparison.OrdinalIgnoreCase)
        )
        {
            var resourceId = ExtractTargetResourceId(target);
            if (
                !string.IsNullOrEmpty(resourceId)
                && !resourceIds.Contains(resourceId)
                && reportedMissingResourceIds.Add(resourceId)
            )
            {
                errors.Add(
                    $"modifiers[{modifierIndex}] ('{modifier.id}') target resource '{resourceId}' references missing resources.id."
                );
            }
        }
    }

    private static string ExtractTargetResourceId(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return string.Empty;

        var normalized = target.Trim();

        if (ParameterizedPathParser.TryParseModifierParameterizedPath(normalized, out var parsed))
            return parsed.ParameterId;

        return string.Empty;
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
        ModifierDefinition modifier,
        int modifierIndex,
        HashSet<string> upgradeIds,
        HashSet<string> buffIds,
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

        if (
            string.Equals(domain, "buff", StringComparison.OrdinalIgnoreCase)
            && !buffIds.Contains(source)
        )
        {
            errors.Add(
                $"modifiers[{modifierIndex}] ('{modifier.id}') source '{source}' does not match any buffs.id."
            );
        }
    }

    private static void ValidatePrestigeConfiguration(
        GameDefinition definition,
        HashSet<string> resourceIds,
        List<string> errors
    )
    {
        var prestige = definition?.prestige;
        if (prestige == null || !prestige.enabled)
            return;

        var prestigeResource = (prestige.prestigeResource ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(prestigeResource))
        {
            errors.Add("prestige.enabled is true but prestige.prestigeResource is empty.");
        }
        else if (!resourceIds.Contains(prestigeResource))
        {
            errors.Add(
                $"prestige.prestigeResource '{prestigeResource}' does not match any resources.id."
            );
        }

        var formula = prestige.formula;
        if (formula == null)
        {
            errors.Add("prestige.enabled is true but prestige.formula is missing.");
        }
        else
        {
            var formulaType = (formula.type ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(formulaType))
            {
                errors.Add("prestige.formula.type is empty.");
            }

            var formulaBasedOn = (formula.basedOn ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(formulaBasedOn))
            {
                errors.Add("prestige.formula.basedOn is empty.");
            }
        }

        var resetScopes = prestige.resetScopes;
        if (resetScopes == null)
        {
            errors.Add("prestige.enabled is true but prestige.resetScopes is missing.");
        }

        var metaUpgrades = prestige.metaUpgrades;
        if (metaUpgrades == null)
            return;

        for (int i = 0; i < metaUpgrades.Length; i++)
        {
            var metaUpgrade = metaUpgrades[i];
            if (metaUpgrade?.computed == null)
                continue;

            var computed = metaUpgrade.computed;
            var computedType = (computed.type ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(computedType))
            {
                errors.Add($"prestige.metaUpgrades[{i}].computed.type is empty.");
            }

            var computedBasedOn = (computed.basedOn ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(computedBasedOn))
            {
                errors.Add($"prestige.metaUpgrades[{i}].computed.basedOn is empty.");
            }
        }
    }

    private static void ValidateFormulaPaths(
        GameDefinition definition,
        HashSet<string> resourceIds,
        List<string> errors
    )
    {
        if (definition == null)
            return;

        if (definition.computedVars != null)
        {
            for (int i = 0; i < definition.computedVars.Count; i++)
            {
                var computedVar = definition.computedVars[i];
                if (computedVar?.dependsOn == null)
                    continue;

                for (int d = 0; d < computedVar.dependsOn.Length; d++)
                {
                    ValidateFormulaPathAndNormalize(
                        ref computedVar.dependsOn[d],
                        resourceIds,
                        $"computedVars[{i}].dependsOn[{d}]",
                        errors
                    );
                }
            }
        }

        if (definition.prestige?.formula != null)
        {
            ValidateFormulaPathAndNormalize(
                ref definition.prestige.formula.basedOn,
                resourceIds,
                "prestige.formula.basedOn",
                errors
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

            ValidateFormulaPathAndNormalize(
                ref metaUpgrade.computed.basedOn,
                resourceIds,
                $"prestige.metaUpgrades[{i}].computed.basedOn",
                errors
            );
        }
    }

    private static void ValidateFormulaPathAndNormalize(
        ref string path,
        HashSet<string> resourceIds,
        string fieldPath,
        List<string> errors
    )
    {
        var raw = (path ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
            return;

        if (!ParameterizedPathParser.TryParseFormulaParameterizedPath(raw, out var parsed))
            return;

        if (!string.Equals(raw, parsed.CanonicalPath, StringComparison.Ordinal))
        {
#if UNITY_EDITOR
            Debug.LogWarning(
                $"GameDefinitionValidator: {fieldPath} '{raw}' normalized to '{parsed.CanonicalPath}'. Prefer bracket form."
            );
#endif
            path = parsed.CanonicalPath;
        }

        if (string.IsNullOrEmpty(parsed.ParameterId) || resourceIds.Contains(parsed.ParameterId))
            return;

        errors.Add($"{fieldPath} resource '{parsed.ParameterId}' references missing resources.id.");
    }

    private static string NormalizeId(string id)
    {
        return (id ?? string.Empty).Trim();
    }
}
