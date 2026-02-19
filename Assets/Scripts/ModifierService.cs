using System;
using System.Collections.Generic;
using System.Linq;
using UniRx;
using UnityEngine;

public sealed class ModifierService : IDisposable
{
    private readonly UpgradeCatalog upgradeCatalog;
    private readonly NodeCatalog nodeCatalog;
    private readonly NodeInstanceCatalog nodeInstanceCatalog;
    private readonly UpgradeService upgradeService;
    private readonly SaveService saveService;

    private readonly Dictionary<string, ModifierEntry> modifiersById = new(StringComparer.Ordinal);
    private readonly List<ModifierEntry> modifiers = new();
    private readonly Dictionary<string, MilestoneEntry> milestonesById = new(
        StringComparer.Ordinal
    );

    private readonly Dictionary<string, List<ModifierEntry>> resolvedModifiersByUpgradeId = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, List<ModifierEntry>> resolvedModifiersByMilestoneId = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, List<ModifierEntry>> activeBuffModifiersBySourceKey = new(
        StringComparer.Ordinal
    );

    private static readonly bool LogRebuildSnapshots = false;

    private readonly Dictionary<string, double> nodeSpeedByInstanceId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> nodeOutputByInstanceId = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, double> nodeOutputByInstanceAndResource = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, double> resourceGainByResourceId = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, bool> automationByInstanceId = new(StringComparer.Ordinal);

    private readonly HashSet<string> warnedKeys = new(StringComparer.Ordinal);
    private readonly Subject<Unit> changed = new();
    private readonly CompositeDisposable disposables = new();

    public ModifierService(
        IReadOnlyList<ModifierEntry> modifiers,
        UpgradeCatalog upgradeCatalog,
        NodeCatalog nodeCatalog,
        NodeInstanceCatalog nodeInstanceCatalog,
        UpgradeService upgradeService,
        SaveService saveService,
        IReadOnlyList<MilestoneEntry> milestones
    )
    {
        this.upgradeCatalog =
            upgradeCatalog ?? throw new ArgumentNullException(nameof(upgradeCatalog));
        this.nodeCatalog = nodeCatalog ?? throw new ArgumentNullException(nameof(nodeCatalog));
        this.nodeInstanceCatalog =
            nodeInstanceCatalog ?? throw new ArgumentNullException(nameof(nodeInstanceCatalog));
        this.upgradeService =
            upgradeService ?? throw new ArgumentNullException(nameof(upgradeService));
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));

        if (modifiers != null)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i];
                if (modifier == null)
                    continue;

                var id = (modifier.id ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(id))
                    continue;
                if (modifiersById.ContainsKey(id))
                    continue;

                this.modifiers.Add(modifier);
                modifiersById[id] = modifier;
            }
        }

        this.modifiers.Sort(
            (a, b) =>
                string.Compare(
                    (a?.id ?? string.Empty).Trim(),
                    (b?.id ?? string.Empty).Trim(),
                    StringComparison.Ordinal
                )
        );

        if (milestones != null)
        {
            for (int i = 0; i < milestones.Count; i++)
            {
                var milestone = milestones[i];
                if (milestone == null)
                    continue;

                var id = (milestone.id ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(id))
                    continue;
                if (milestonesById.ContainsKey(id))
                    continue;

                milestonesById[id] = milestone;
            }
        }

        upgradeService
            .PurchasedStateChangedAsObservable()
            .Subscribe(triggerUpgradeId => RebuildActiveModifiers(triggerUpgradeId))
            .AddTo(disposables);

        RebuildActiveModifiers("startup");
    }

    public IObservable<Unit> Changed => changed;

    public double GetNodeSpeedMultiplier(string nodeInstanceId)
    {
        var key = NormalizeId(nodeInstanceId, nameof(nodeInstanceId));
        return nodeSpeedByInstanceId.TryGetValue(key, out var value) ? value : 1.0;
    }

    public double GetNodeOutputMultiplier(string nodeInstanceId, string resourceId = null)
    {
        var key = NormalizeId(nodeInstanceId, nameof(nodeInstanceId));
        var resourceKey = (resourceId ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(resourceKey))
        {
            var instanceAndResourceKey = BuildInstanceResourceKey(key, resourceKey);
            if (
                nodeOutputByInstanceAndResource.TryGetValue(
                    instanceAndResourceKey,
                    out var scopedValue
                )
            )
            {
                return scopedValue;
            }
        }

        return nodeOutputByInstanceId.TryGetValue(key, out var value) ? value : 1.0;
    }

    public double GetResourceGainMultiplier(string resourceId)
    {
        var key = NormalizeId(resourceId, nameof(resourceId));
        return resourceGainByResourceId.TryGetValue(key, out var value) ? value : 1.0;
    }

    public bool IsNodeAutomationEnabled(string nodeInstanceId)
    {
        var key = NormalizeId(nodeInstanceId, nameof(nodeInstanceId));
        return automationByInstanceId.TryGetValue(key, out var enabled) && enabled;
    }

    public void SetBuffModifierSource(
        string sourceKey,
        string buffId,
        IReadOnlyList<EffectItem> effects
    )
    {
        var key = NormalizeId(sourceKey, nameof(sourceKey));
        var normalizedBuffId = NormalizeId(buffId, nameof(buffId));

        if (effects == null || effects.Count == 0)
        {
            throw new InvalidOperationException(
                $"ModifierService: buff '{normalizedBuffId}' has no effects."
            );
        }

        var resolved = new List<ModifierEntry>();
        for (int i = 0; i < effects.Count; i++)
        {
            var effect = effects[i];
            var modifierId = (effect?.modifierId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(modifierId))
            {
                throw new InvalidOperationException(
                    $"ModifierService: buff '{normalizedBuffId}' has empty effects[{i}].modifierId."
                );
            }

            if (!modifiersById.TryGetValue(modifierId, out var modifier) || modifier == null)
            {
                throw new InvalidOperationException(
                    $"ModifierService: buff '{normalizedBuffId}' references missing modifierId '{modifierId}'."
                );
            }

            ValidateBuffModifierOrThrow(normalizedBuffId, modifier);
            resolved.Add(modifier);
        }

        resolved.Sort(
            (a, b) =>
                string.Compare(
                    (a?.id ?? string.Empty).Trim(),
                    (b?.id ?? string.Empty).Trim(),
                    StringComparison.Ordinal
                )
        );

        activeBuffModifiersBySourceKey[key] = resolved;
    }

    public void RemoveBuffModifierSource(string sourceKey)
    {
        var key = NormalizeId(sourceKey, nameof(sourceKey));
        activeBuffModifiersBySourceKey.Remove(key);
    }

    public void RebuildActiveModifiers(string triggerUpgradeId = null)
    {
        var trigger = string.IsNullOrWhiteSpace(triggerUpgradeId)
            ? "unknown"
            : triggerUpgradeId.Trim();

        var speedAcc = new Dictionary<string, ScalarAccumulator>(StringComparer.Ordinal);
        var outputAcc = new Dictionary<string, ScalarAccumulator>(StringComparer.Ordinal);
        var outputByResourceAcc = new Dictionary<string, ScalarAccumulator>(StringComparer.Ordinal);
        var resourceGainAcc = new Dictionary<string, ScalarAccumulator>(StringComparer.Ordinal);
        var automationSetByInstanceId = new Dictionary<string, bool>(StringComparer.Ordinal);

        var active = BuildActiveModifierSequenceWithCounts();
        for (int i = 0; i < active.Count; i++)
        {
            var entry = active[i];
            var modifier = entry.Modifier;
            if (modifier == null)
                continue;

            if (!TryParseTarget(modifier, out var targetKind, out var targetResourceId))
                continue;
            if (!TryResolveOperation(modifier, targetKind, out var operation))
                continue;
            if (!TryResolveTargetInstanceIds(modifier, targetKind, out var targetInstanceIds))
                continue;

            // Repeatable purchases: avoid duplicating modifiers N times.
            // multiply: applying the same multiplier N times is value^N.
            // set: repeat count is irrelevant (deterministic last-applied wins).
            var scalarValue = modifier.value;
            if (operation == ModifierOp.Multiply && entry.PurchaseCount > 1)
                scalarValue = Math.Pow(modifier.value, entry.PurchaseCount);

            switch (targetKind)
            {
                case TargetKind.NodeSpeed:
                    ApplyScalar(
                        operation,
                        scalarValue,
                        targetInstanceIds,
                        speedAcc,
                        "node.speedMultiplier"
                    );
                    break;

                case TargetKind.NodeOutput:
                    if (!string.IsNullOrEmpty(targetResourceId))
                    {
                        ApplyScalarToInstanceResource(
                            operation,
                            scalarValue,
                            targetInstanceIds,
                            targetResourceId,
                            outputByResourceAcc,
                            "node.outputMultiplier"
                        );
                    }
                    else
                    {
                        ApplyScalar(
                            operation,
                            scalarValue,
                            targetInstanceIds,
                            outputAcc,
                            "node.outputMultiplier"
                        );
                    }

                    break;

                case TargetKind.ResourceGain:
                    if (string.IsNullOrEmpty(targetResourceId))
                    {
                        WarnOnce(
                            $"{modifier.id}:resourceGain.missingResource",
                            $"[ModifierService] Skipping modifier '{modifier.id}': resourceGain target requires resource id."
                        );
                        break;
                    }

                    if (!IsGlobalOrResourceScope(modifier))
                    {
                        WarnOnce(
                            $"{modifier.id}:resourceGain.scope",
                            $"[ModifierService] Skipping modifier '{modifier.id}': resourceGain only supports global/resource scope in this slice."
                        );
                        break;
                    }

                    ApplyScalar(
                        operation,
                        scalarValue,
                        new[] { targetResourceId },
                        resourceGainAcc,
                        "resourceGain"
                    );
                    break;

                case TargetKind.AutomationPolicy:
                    if (operation != ModifierOp.Set)
                    {
                        WarnOnce(
                            $"{modifier.id}:automation.op",
                            $"[ModifierService] Skipping modifier '{modifier.id}': automation targets only support 'set'."
                        );
                        break;
                    }

                    ApplyAutomationSet(modifier, targetInstanceIds, automationSetByInstanceId);
                    break;
            }
        }

        BuildScalarMap(speedAcc, nodeSpeedByInstanceId);
        BuildScalarMap(outputAcc, nodeOutputByInstanceId);
        BuildScalarMap(outputByResourceAcc, nodeOutputByInstanceAndResource);
        BuildScalarMap(resourceGainAcc, resourceGainByResourceId);

        automationByInstanceId.Clear();
        foreach (var kv in automationSetByInstanceId)
            automationByInstanceId[kv.Key] = kv.Value;

        if (LogRebuildSnapshots)
        {
            if (resourceGainByResourceId.TryGetValue("currencySoft", out var softMult))
            {
                Debug.Log(
                    $"[ModifierService] trigger='{trigger}' resourceGain.currencySoft multiplier: {softMult:0.###}"
                );
            }

            foreach (var instance in nodeInstanceCatalog.NodeInstances)
            {
                var instanceId = (instance?.id ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(instanceId))
                    continue;

                var speed = GetNodeSpeedMultiplier(instanceId);
                if (Math.Abs(speed - 1.0) > 0.000001)
                {
                    Debug.Log(
                        $"[ModifierService] trigger='{trigger}' speed multiplier for '{instanceId}': {speed:0.###}"
                    );
                }

                var output = GetNodeOutputMultiplier(instanceId);
                if (Math.Abs(output - 1.0) > 0.000001)
                {
                    Debug.Log(
                        $"[ModifierService] trigger='{trigger}' output multiplier for '{instanceId}': {output:0.###}"
                    );
                }

                var nodeId = (instance.nodeId ?? string.Empty).Trim();
                if (
                    !string.IsNullOrEmpty(nodeId)
                    && nodeCatalog.TryGet(nodeId, out var node)
                    && node?.outputs != null
                )
                {
                    for (int i = 0; i < node.outputs.Count; i++)
                    {
                        var resourceId = (node.outputs[i]?.resource ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(resourceId))
                            continue;

                        var scopedKey = BuildInstanceResourceKey(instanceId, resourceId);
                        if (!nodeOutputByInstanceAndResource.ContainsKey(scopedKey))
                            continue;

                        var scopedOutput = GetNodeOutputMultiplier(instanceId, resourceId);
                        if (Math.Abs(scopedOutput - 1.0) > 0.000001)
                        {
                            Debug.Log(
                                $"[ModifierService] trigger='{trigger}' output multiplier for '{instanceId}' resource '{resourceId}': {scopedOutput:0.###}"
                            );
                        }
                    }
                }
            }
        }

        changed.OnNext(Unit.Default);
    }

    public void Dispose()
    {
        changed.OnCompleted();
        changed.Dispose();
        disposables.Dispose();
    }

    private struct ActiveModifier
    {
        public ModifierEntry Modifier;
        public int PurchaseCount;
    }

    private List<ActiveModifier> BuildActiveModifierSequenceWithCounts()
    {
        var active = new List<ActiveModifier>();
        var purchased = upgradeService.GetPurchasedCountsSnapshot();
        if (purchased != null && purchased.Count > 0)
        {
            var upgradesOrdered = purchased.Keys.OrderBy(id => id, StringComparer.Ordinal);
            foreach (var upgradeId in upgradesOrdered)
            {
                if (!purchased.TryGetValue(upgradeId, out var count) || count <= 0)
                    continue;

                UpgradeEntry upgrade;
                try
                {
                    upgrade = upgradeCatalog.GetRequired(upgradeId);
                }
                catch
                {
                    WarnOnce(
                        $"upgrade.missing:{upgradeId}",
                        $"[ModifierService] Purchased upgrade '{upgradeId}' not found in UpgradeCatalog."
                    );
                    continue;
                }

                var resolved = ResolveModifiersForUpgrade(upgrade);
                for (int i = 0; i < resolved.Count; i++)
                {
                    active.Add(
                        new ActiveModifier { Modifier = resolved[i], PurchaseCount = count }
                    );
                }
            }
        }

        if (activeBuffModifiersBySourceKey.Count > 0)
        {
            var orderedSources = activeBuffModifiersBySourceKey.Keys.OrderBy(
                key => key,
                StringComparer.Ordinal
            );
            foreach (var sourceKey in orderedSources)
            {
                if (!activeBuffModifiersBySourceKey.TryGetValue(sourceKey, out var buffModifiers))
                    continue;
                if (buffModifiers == null || buffModifiers.Count == 0)
                    continue;

                for (int i = 0; i < buffModifiers.Count; i++)
                {
                    active.Add(
                        new ActiveModifier { Modifier = buffModifiers[i], PurchaseCount = 1 }
                    );
                }
            }
        }

        var firedMilestones = saveService.Data?.FiredMilestoneIds;
        if (firedMilestones == null || firedMilestones.Count == 0)
            return active;

        var milestonesOrdered = firedMilestones.OrderBy(id => id, StringComparer.Ordinal);
        foreach (var milestoneId in milestonesOrdered)
        {
            if (!milestonesById.TryGetValue(milestoneId, out var milestone) || milestone == null)
            {
                WarnOnce(
                    $"milestone.missing:{milestoneId}",
                    $"[ModifierService] Fired milestone '{milestoneId}' not found in GameDefinition milestones."
                );
                continue;
            }

            var resolved = ResolveModifiersForMilestone(milestone);
            for (int i = 0; i < resolved.Count; i++)
                active.Add(new ActiveModifier { Modifier = resolved[i], PurchaseCount = 1 });
        }

        return active;
    }

    private List<ModifierEntry> ResolveModifiersForUpgrade(UpgradeEntry upgrade)
    {
        var resolved = new List<ModifierEntry>();
        if (upgrade == null)
            return resolved;

        var upgradeId = (upgrade.id ?? string.Empty).Trim();
        if (
            !string.IsNullOrEmpty(upgradeId)
            && resolvedModifiersByUpgradeId.TryGetValue(upgradeId, out var cached)
        )
            return cached;

        bool hasInvalidReference = false;

        if (upgrade.effects != null && upgrade.effects.Length > 0)
        {
            for (int i = 0; i < upgrade.effects.Length; i++)
            {
                var effect = upgrade.effects[i];
                if (effect == null)
                {
                    hasInvalidReference = true;
                    WarnOnce(
                        $"upgrade.invalid_effect:{upgrade.id}:{i}",
                        $"[ModifierService] Upgrade '{upgrade.id}' has null effects[{i}]."
                    );
                    continue;
                }

                var modifierId = (effect.modifierId ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(modifierId))
                {
                    hasInvalidReference = true;
                    WarnOnce(
                        $"upgrade.invalid_modifier_id:{upgrade.id}:{i}",
                        $"[ModifierService] Upgrade '{upgrade.id}' has empty effects[{i}].modifierId."
                    );
                    continue;
                }

                if (!modifiersById.TryGetValue(modifierId, out var modifier) || modifier == null)
                {
                    hasInvalidReference = true;
                    WarnOnce(
                        $"upgrade.missing_modifier:{upgrade.id}:{modifierId}",
                        $"[ModifierService] Upgrade '{upgrade.id}' references missing modifierId '{modifierId}'."
                    );
                    continue;
                }

                resolved.Add(modifier);
            }
        }
        else
        {
            hasInvalidReference = true;
            WarnOnce(
                $"upgrade.no_effects:{upgrade.id}",
                $"[ModifierService] Upgrade '{upgrade.id}' has no effects[].modifierId entries."
            );
        }

        if (hasInvalidReference)
        {
            WarnOnce(
                $"upgrade.invalid_not_applied:{upgrade.id}",
                $"[ModifierService] Upgrade '{upgrade.id}' has invalid modifier references and will not be applied."
            );
            resolved.Clear();
        }

        resolved.Sort(
            (a, b) =>
                string.Compare(
                    (a?.id ?? string.Empty).Trim(),
                    (b?.id ?? string.Empty).Trim(),
                    StringComparison.Ordinal
                )
        );

        if (resolved.Count == 0)
        {
            WarnOnce(
                $"upgrade.no_modifiers:{upgrade.id}",
                $"[ModifierService] Purchased upgrade '{upgrade.id}' has no resolvable modifiers."
            );
        }

        if (!string.IsNullOrEmpty(upgradeId))
            resolvedModifiersByUpgradeId[upgradeId] = resolved;

        return resolved;
    }

    private List<ModifierEntry> ResolveModifiersForMilestone(MilestoneEntry milestone)
    {
        var resolved = new List<ModifierEntry>();
        if (milestone == null)
            return resolved;

        var milestoneId = (milestone.id ?? string.Empty).Trim();
        if (
            !string.IsNullOrEmpty(milestoneId)
            && resolvedModifiersByMilestoneId.TryGetValue(milestoneId, out var cached)
        )
            return cached;

        if (milestone.grantEffects != null && milestone.grantEffects.Length > 0)
        {
            for (int i = 0; i < milestone.grantEffects.Length; i++)
            {
                var modifierId = (milestone.grantEffects[i]?.modifierId ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(modifierId))
                    continue;

                if (modifiersById.TryGetValue(modifierId, out var modifier) && modifier != null)
                    resolved.Add(modifier);
            }
        }

        if (resolved.Count == 0)
        {
            var source = (milestone.id ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(source))
            {
                for (int i = 0; i < modifiers.Count; i++)
                {
                    var modifier = modifiers[i];
                    if (
                        string.Equals(
                            (modifier.source ?? string.Empty).Trim(),
                            source,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        resolved.Add(modifier);
                    }
                }
            }
        }

        resolved.Sort(
            (a, b) =>
                string.Compare(
                    (a?.id ?? string.Empty).Trim(),
                    (b?.id ?? string.Empty).Trim(),
                    StringComparison.Ordinal
                )
        );

        if (resolved.Count == 0)
        {
            WarnOnce(
                $"milestone.no_modifiers:{milestone.id}",
                $"[ModifierService] Fired milestone '{milestone.id}' has no resolvable modifiers."
            );
        }

        if (!string.IsNullOrEmpty(milestoneId))
            resolvedModifiersByMilestoneId[milestoneId] = resolved;

        return resolved;
    }

    private void ValidateBuffModifierOrThrow(string buffId, ModifierEntry modifier)
    {
        if (modifier == null)
            throw new InvalidOperationException($"ModifierService: buff '{buffId}' has null modifier.");

        if (!TryParseTarget(modifier, out var targetKind, out _))
        {
            throw new InvalidOperationException(
                $"ModifierService: buff '{buffId}' modifier '{modifier.id}' has unsupported target '{modifier.target}'."
            );
        }

        if (!TryResolveOperation(modifier, targetKind, out _))
        {
            throw new InvalidOperationException(
                $"ModifierService: buff '{buffId}' modifier '{modifier.id}' has unsupported operation '{modifier.operation}'."
            );
        }

        if (!TryResolveTargetInstanceIds(modifier, targetKind, out _))
        {
            throw new InvalidOperationException(
                $"ModifierService: buff '{buffId}' modifier '{modifier.id}' has unsupported scope or target mapping."
            );
        }
    }

    private bool TryResolveTargetInstanceIds(
        ModifierEntry modifier,
        TargetKind targetKind,
        out List<string> instanceIds
    )
    {
        instanceIds = new List<string>();
        var scopeKind = (modifier.scope?.kind ?? string.Empty).Trim();

        if (string.Equals(scopeKind, "global", StringComparison.OrdinalIgnoreCase))
        {
            if (targetKind == TargetKind.ResourceGain)
                return true;

            foreach (var instance in nodeInstanceCatalog.NodeInstances)
            {
                var id = (instance?.id ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(id))
                    instanceIds.Add(id);
            }

            return true;
        }

        if (string.Equals(scopeKind, "resource", StringComparison.OrdinalIgnoreCase))
        {
            if (targetKind == TargetKind.ResourceGain)
                return true;

            WarnOnce(
                $"{modifier.id}:scope.resource.unsupported",
                $"[ModifierService] Skipping modifier '{modifier.id}': resource scope is only supported for resourceGain in this slice."
            );
            return false;
        }

        if (
            string.Equals(scopeKind, "node", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scopeKind, "nodeTag", StringComparison.OrdinalIgnoreCase)
        )
        {
            var scopeNodeId = (modifier.scope?.nodeId ?? string.Empty).Trim();
            var scopeNodeTag = (modifier.scope?.nodeTag ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(scopeNodeId))
            {
                var scoped = nodeInstanceCatalog.GetForNode(scopeNodeId);
                if (scoped.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"ModifierService: Modifier '{modifier.id}' references missing nodeId '{scopeNodeId}'."
                    );
                }

                for (int i = 0; i < scoped.Count; i++)
                {
                    var instanceId = (scoped[i]?.id ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(instanceId))
                        instanceIds.Add(instanceId);
                }

                return true;
            }

            if (!string.IsNullOrEmpty(scopeNodeTag))
            {
                var nodes = nodeCatalog.GetByTag(scopeNodeTag);
                if (nodes == null || nodes.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"ModifierService: Modifier '{modifier.id}' references missing nodeTag '{scopeNodeTag}'."
                    );
                }

                for (int i = 0; i < nodes.Count; i++)
                {
                    var nodeId = (nodes[i]?.id ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(nodeId))
                        continue;

                    var scoped = nodeInstanceCatalog.GetForNode(nodeId);
                    for (int j = 0; j < scoped.Count; j++)
                    {
                        var instanceId = (scoped[j]?.id ?? string.Empty).Trim();
                        if (!string.IsNullOrEmpty(instanceId))
                            instanceIds.Add(instanceId);
                    }
                }

                if (instanceIds.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"ModifierService: Modifier '{modifier.id}' nodeTag '{scopeNodeTag}' resolved no node instances."
                    );
                }

                return true;
            }

            throw new InvalidOperationException(
                $"ModifierService: Modifier '{modifier.id}' node scope requires nodeId or nodeTag."
            );
        }

        WarnOnce(
            $"{modifier.id}:scope.unsupported",
            $"[ModifierService] Skipping modifier '{modifier.id}': unsupported scope.kind '{scopeKind}'."
        );
        return false;
    }

    private bool TryResolveOperation(
        ModifierEntry modifier,
        TargetKind targetKind,
        out ModifierOp op
    )
    {
        op = ModifierOp.Multiply;
        var raw = (modifier.operation ?? string.Empty).Trim();
        if (string.Equals(raw, "multiply", StringComparison.OrdinalIgnoreCase))
        {
            op = ModifierOp.Multiply;
            return true;
        }

        if (string.Equals(raw, "add", StringComparison.OrdinalIgnoreCase))
        {
            // Minimal slice: we do not apply additive multipliers.
            if (
                targetKind == TargetKind.ResourceGain
                || targetKind == TargetKind.NodeSpeed
                || targetKind == TargetKind.NodeOutput
            )
            {
                WarnOnce(
                    $"{modifier.id}:add.unsupported",
                    $"[ModifierService] Skipping modifier '{modifier.id}': 'add' is not executed for multiplier targets in this slice."
                );
                return false;
            }

            op = ModifierOp.Add;
            return true;
        }

        if (string.Equals(raw, "set", StringComparison.OrdinalIgnoreCase))
        {
            op = ModifierOp.Set;
            return true;
        }

        WarnOnce(
            $"{modifier.id}:op.unsupported",
            $"[ModifierService] Skipping modifier '{modifier.id}': unsupported operation '{raw}'."
        );
        return false;
    }

    private bool TryParseTarget(
        ModifierEntry modifier,
        out TargetKind targetKind,
        out string targetResourceId
    )
    {
        targetKind = TargetKind.Unknown;
        targetResourceId = null;

        var target = (modifier.target ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(target))
        {
            WarnOnce(
                $"{modifier.id}:target.empty",
                $"[ModifierService] Skipping modifier '{modifier.id}': target is empty."
            );
            return false;
        }

        if (
            string.Equals(target, "nodeSpeedMultiplier", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "node.speedMultiplier", StringComparison.OrdinalIgnoreCase)
        )
        {
            targetKind = TargetKind.NodeSpeed;
            return true;
        }

        if (
            string.Equals(target, "nodeOutput", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "node.outputMultiplier", StringComparison.OrdinalIgnoreCase)
        )
        {
            targetKind = TargetKind.NodeOutput;
            targetResourceId = (modifier.scope?.resource ?? string.Empty).Trim();
            return true;
        }

        if (
            target.StartsWith("nodeOutput.", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("node.outputMultiplier.", StringComparison.OrdinalIgnoreCase)
        )
        {
            targetKind = TargetKind.NodeOutput;
            var dot = target.LastIndexOf('.');
            if (dot >= 0 && dot < target.Length - 1)
                targetResourceId = target.Substring(dot + 1).Trim();
            return true;
        }

        if (string.Equals(target, "automation.policy", StringComparison.OrdinalIgnoreCase))
        {
            targetKind = TargetKind.AutomationPolicy;
            return true;
        }

        if (
            string.Equals(target, "automation.autoCollect", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "automation.autoRestart", StringComparison.OrdinalIgnoreCase)
        )
        {
            targetKind = TargetKind.AutomationPolicy;
            return true;
        }

        if (target.StartsWith("resourceGain.", StringComparison.OrdinalIgnoreCase))
        {
            targetKind = TargetKind.ResourceGain;
            var dot = target.IndexOf('.');
            if (dot >= 0 && dot < target.Length - 1)
                targetResourceId = target.Substring(dot + 1).Trim();
            return true;
        }

        if (
            target.StartsWith("resourceGain[", StringComparison.OrdinalIgnoreCase)
            && target.EndsWith("]")
        )
        {
            targetKind = TargetKind.ResourceGain;
            targetResourceId = target.Substring("resourceGain[".Length);
            targetResourceId = targetResourceId.Substring(0, targetResourceId.Length - 1).Trim();
            return true;
        }

        WarnOnce(
            $"{modifier.id}:target.unsupported",
            $"[ModifierService] Skipping modifier '{modifier.id}': unsupported target '{target}'."
        );
        return false;
    }

    private static bool IsGlobalOrResourceScope(ModifierEntry modifier)
    {
        var kind = (modifier.scope?.kind ?? string.Empty).Trim();
        return string.Equals(kind, "global", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "resource", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyScalar(
        ModifierOp op,
        double value,
        IReadOnlyList<string> keys,
        IDictionary<string, ScalarAccumulator> accByKey,
        string targetName
    )
    {
        for (int i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (!accByKey.TryGetValue(key, out var acc))
                acc = ScalarAccumulator.Identity;

            var previousSet = acc.HasSet ? acc.SetValue : double.NaN;
            acc.Apply(op, value);
            if (
                !double.IsNaN(previousSet)
                && acc.HasSet
                && Math.Abs(previousSet - acc.SetValue) > 0.000001
            )
            {
                WarnOnce(
                    $"set.conflict:{targetName}:{key}",
                    $"[ModifierService] Conflicting 'set' modifiers for {targetName} at '{key}'. Deterministic last-applied wins."
                );
            }

            accByKey[key] = acc;
        }
    }

    private void ApplyScalarToInstanceResource(
        ModifierOp op,
        double value,
        IReadOnlyList<string> instanceIds,
        string resourceId,
        IDictionary<string, ScalarAccumulator> accByKey,
        string targetName
    )
    {
        for (int i = 0; i < instanceIds.Count; i++)
        {
            var key = BuildInstanceResourceKey(instanceIds[i], resourceId);
            if (!accByKey.TryGetValue(key, out var acc))
                acc = ScalarAccumulator.Identity;

            var previousSet = acc.HasSet ? acc.SetValue : double.NaN;
            acc.Apply(op, value);
            if (
                !double.IsNaN(previousSet)
                && acc.HasSet
                && Math.Abs(previousSet - acc.SetValue) > 0.000001
            )
            {
                WarnOnce(
                    $"set.conflict:{targetName}:{key}",
                    $"[ModifierService] Conflicting 'set' modifiers for {targetName} at '{key}'. Deterministic last-applied wins."
                );
            }

            accByKey[key] = acc;
        }
    }

    private void ApplyAutomationSet(
        ModifierEntry modifier,
        IReadOnlyList<string> instanceIds,
        IDictionary<string, bool> automationByKey
    )
    {
        bool value = true;
        var target = (modifier.target ?? string.Empty).Trim();
        if (
            string.Equals(target, "automation.autoCollect", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "automation.autoRestart", StringComparison.OrdinalIgnoreCase)
        )
        {
            value = modifier.value > 0;
        }

        for (int i = 0; i < instanceIds.Count; i++)
        {
            var key = instanceIds[i];
            if (automationByKey.TryGetValue(key, out var previous) && previous != value)
            {
                WarnOnce(
                    $"set.conflict:automation:{key}",
                    $"[ModifierService] Conflicting automation 'set' modifiers at '{key}'. Deterministic last-applied wins."
                );
            }

            automationByKey[key] = value;
        }
    }

    private static void BuildScalarMap(
        IReadOnlyDictionary<string, ScalarAccumulator> source,
        IDictionary<string, double> destination
    )
    {
        destination.Clear();
        foreach (var kv in source)
        {
            destination[kv.Key] = kv.Value.Resolve();
        }
    }

    private static string NormalizeId(string value, string name)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized))
            throw new ArgumentException($"{name} cannot be empty.", name);
        return normalized;
    }

    private static string BuildInstanceResourceKey(string instanceId, string resourceId)
    {
        return $"{instanceId}|{resourceId}";
    }

    private void WarnOnce(string key, string message)
    {
        if (!warnedKeys.Add(key))
            return;

        Debug.LogWarning(message);
    }

    private enum TargetKind
    {
        Unknown,
        NodeSpeed,
        NodeOutput,
        ResourceGain,
        AutomationPolicy,
    }

    private enum ModifierOp
    {
        Multiply,
        Add,
        Set,
    }

    private struct ScalarAccumulator
    {
        public static readonly ScalarAccumulator Identity = new ScalarAccumulator
        {
            Multiply = 1.0,
            SetValue = 0.0,
            HasSet = false,
        };

        public double Multiply;
        public double SetValue;
        public bool HasSet;

        public void Apply(ModifierOp op, double value)
        {
            switch (op)
            {
                case ModifierOp.Multiply:
                    Multiply *= value;
                    break;
                case ModifierOp.Set:
                    SetValue = value;
                    HasSet = true;
                    break;
                case ModifierOp.Add:
                    break;
            }
        }

        public double Resolve()
        {
            return HasSet ? SetValue : Multiply;
        }
    }
}
