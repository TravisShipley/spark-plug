using System;
using System.Collections.Generic;
using System.Globalization;
using UniRx;
using UnityEngine;
using Object = UnityEngine.Object;

public class GeneratorListComposer
{
    private readonly GameObject generatorUIRootPrefab;
    private readonly Transform generatorUIContainer;
    private readonly WalletService walletService;
    private readonly WalletViewModel walletViewModel;
    private readonly TickService tickService;
    private readonly ModifierService modifierService;
    private readonly UiServiceRegistry uiService;
    private readonly SaveService saveService;
    private readonly UpgradeService upgradeService;
    private readonly GameDefinitionService gameDefinitionService;
    private readonly CompositeDisposable disposables;

    private readonly List<GeneratorModel> generatorModels;
    private readonly List<GeneratorService> generatorServices;
    private readonly List<GeneratorViewModel> generatorViewModels;

    public GeneratorListComposer(
        GameObject generatorUIRootPrefab,
        Transform generatorUIContainer,
        WalletService walletService,
        WalletViewModel walletViewModel,
        TickService tickService,
        ModifierService modifierService,
        UiServiceRegistry uiService,
        SaveService saveService,
        UpgradeService upgradeService,
        GameDefinitionService gameDefinitionService,
        CompositeDisposable disposables,
        List<GeneratorModel> generatorModels,
        List<GeneratorService> generatorServices,
        List<GeneratorViewModel> generatorViewModels
    )
    {
        this.generatorUIRootPrefab = generatorUIRootPrefab;
        this.generatorUIContainer = generatorUIContainer;
        this.walletService = walletService;
        this.walletViewModel = walletViewModel;
        this.tickService = tickService;
        this.modifierService = modifierService;
        this.uiService = uiService;
        this.saveService = saveService;
        this.upgradeService = upgradeService;
        this.gameDefinitionService = gameDefinitionService;
        this.disposables = disposables;
        this.generatorModels = generatorModels;
        this.generatorServices = generatorServices;
        this.generatorViewModels = generatorViewModels;
    }

    public void Compose()
    {
        var instances = gameDefinitionService?.NodeInstances;
        if (instances == null || instances.Count == 0)
        {
            Debug.LogError(
                "GeneratorListComposer: No node instances found in GameDefinitionService."
            );
            return;
        }

        for (int i = 0; i < instances.Count; i++)
        {
            var nodeInstance = instances[i];
            if (nodeInstance == null)
                continue;

            var instanceId = (nodeInstance.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(instanceId))
                continue;

            var nodeTypeId = (nodeInstance.nodeId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(nodeTypeId))
            {
                Debug.LogWarning(
                    $"GeneratorListComposer: Node instance '{instanceId}' is missing nodeId."
                );
                continue;
            }

            if (!gameDefinitionService.TryGetNode(nodeTypeId, out var nodeDef) || nodeDef == null)
            {
                Debug.LogWarning(
                    $"GeneratorListComposer: Node instance '{instanceId}' references missing nodeId '{nodeTypeId}'."
                );
                continue;
            }

            var generatorDefinition = CreateRuntimeDefinition(nodeDef, nodeInstance);

            var generatorUI = Object.Instantiate(generatorUIRootPrefab, generatorUIContainer);
            generatorUI.name = $"Generator_{instanceId}";

            var generatorView = generatorUI.GetComponent<GeneratorView>();
            if (generatorView == null)
            {
                Debug.LogError(
                    $"GameCompositionRoot: Generator UI root prefab is missing a GeneratorView (instance '{instanceId}').",
                    generatorUI
                );
                continue;
            }

            ComposeSingle(generatorDefinition, nodeInstance, generatorView, nodeTypeId);
        }
    }

    private void ComposeSingle(
        GeneratorDefinition generatorDefinition,
        NodeInstanceDefinition nodeInstance,
        GeneratorView generatorView,
        string nodeTypeId
    )
    {
        string id = generatorDefinition.Id;

        LoadGeneratorState(
            saveService.Data,
            id,
            nodeInstance,
            out bool isOwned,
            out bool isAutomated,
            out int level
        );

        var model = new GeneratorModel
        {
            Id = id,
            Level = level,
            IsOwned = isOwned,
            IsAutomated = isAutomated,
            CycleElapsedSeconds = 0,
        };

        generatorModels.Add(model);

        var service = new GeneratorService(
            model,
            generatorDefinition,
            walletService,
            tickService,
            modifierService
        );

        var generatorViewModel = new GeneratorViewModel(model, generatorDefinition, service);

        generatorServices.Add(service);
        generatorViewModels.Add(generatorViewModel);

        generatorView.Bind(generatorViewModel, service, walletViewModel);

        var generatorButtonView = generatorView.GetComponentInChildren<GeneratorButtonView>(true);
        if (generatorButtonView != null)
            generatorButtonView.Bind(generatorViewModel);

        // Register by instance id (authoritative runtime id).
        uiService.RegisterGenerator(model.Id, service);
        // Compatibility path: allow lookups by node type id when there is a 1:1 instance mapping.
        if (!string.IsNullOrWhiteSpace(nodeTypeId))
            uiService.RegisterGenerator(nodeTypeId.Trim(), service);

        WireGeneratorPersistence(id, service);
    }

    private GeneratorDefinition CreateRuntimeDefinition(
        NodeDefinition nodeDef,
        NodeInstanceDefinition nodeInstance
    )
    {
        var definition = ScriptableObject.CreateInstance<GeneratorDefinition>();

        definition.Id = (nodeInstance.id ?? string.Empty).Trim();

        var displayNameOverride = (nodeInstance.displayNameOverride ?? string.Empty).Trim();
        definition.DisplayName = string.IsNullOrEmpty(displayNameOverride)
            ? nodeDef.displayName
            : displayNameOverride;

        definition.BaseCycleDurationSeconds = Math.Max(
            0.0001,
            nodeDef?.cycle?.baseDurationSeconds ?? 1.0
        );

        // v1 runtime mapping: use first output entry (fallback to 0)
        var output =
            nodeDef?.outputs != null && nodeDef.outputs.Count > 0 ? nodeDef.outputs[0] : null;
        var payout = output?.basePayout ?? 0.0;
        var perSecond = output?.basePerSecond ?? 0.0;
        definition.BaseOutputPerCycle =
            payout > 0 ? payout : perSecond * definition.BaseCycleDurationSeconds;
        definition.OutputResourceId = (output?.resource ?? string.Empty).Trim();
        definition.LevelCostResourceId = (nodeDef?.leveling?.levelResource ?? string.Empty).Trim();

        definition.BaseLevelCost = Math.Max(0.0, nodeDef?.leveling?.priceCurve?.basePrice ?? 0.0);
        definition.LevelCostGrowth = Math.Max(1.0, nodeDef?.leveling?.priceCurve?.growth ?? 1.0);

        definition.AutomationCost = ResolveAutomationCostForNode(
            nodeDef?.id,
            definition.AutomationCost,
            definition.LevelCostResourceId,
            out var automationCostResourceId
        );
        definition.AutomationCostResourceId = automationCostResourceId;
        definition.MilestoneLevels = ResolveMilestoneLevelsForNode(
            nodeDef?.id,
            nodeInstance?.zoneId
        );

        return definition;
    }

    private int[] ResolveMilestoneLevelsForNode(string nodeId, string zoneId)
    {
        var normalizedNodeId = (nodeId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalizedNodeId))
            return Array.Empty<int>();

        var normalizedZoneId = (zoneId ?? string.Empty).Trim();
        var milestones = gameDefinitionService?.Milestones;
        if (milestones == null || milestones.Count == 0)
            return Array.Empty<int>();

        var levels = new List<int>();
        for (int i = 0; i < milestones.Count; i++)
        {
            var milestone = milestones[i];
            if (milestone == null)
                continue;

            var milestoneNodeId = (milestone.nodeId ?? string.Empty).Trim();
            if (!string.Equals(milestoneNodeId, normalizedNodeId, StringComparison.Ordinal))
                continue;

            var milestoneZoneId = (milestone.zoneId ?? string.Empty).Trim();
            if (
                !string.IsNullOrEmpty(milestoneZoneId)
                && !string.Equals(milestoneZoneId, normalizedZoneId, StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (milestone.atLevel <= 0)
                continue;

            levels.Add(milestone.atLevel);
        }

        levels.Sort();
        return levels.ToArray();
    }

    private double ResolveAutomationCostForNode(
        string nodeId,
        double fallback,
        string fallbackResourceId,
        out string costResourceId
    )
    {
        costResourceId = (fallbackResourceId ?? string.Empty).Trim();
        var normalizedNodeId = (nodeId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalizedNodeId))
            return Math.Max(0.0, fallback);

        var upgrades = gameDefinitionService?.Upgrades;
        var modifiers = gameDefinitionService?.Modifiers;
        if (upgrades == null || upgrades.Count == 0 || modifiers == null || modifiers.Count == 0)
            return Math.Max(0.0, fallback);

        for (int u = 0; u < upgrades.Count; u++)
        {
            var upgrade = upgrades[u];
            if (upgrade == null || !HasTag(upgrade.tags, "automation"))
                continue;

            var effects = upgrade.effects;
            if (effects == null || effects.Length == 0)
                continue;

            for (int e = 0; e < effects.Length; e++)
            {
                var modifierId = (effects[e]?.modifierId ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(modifierId))
                    continue;

                var modifier = FindModifierById(modifiers, modifierId);
                if (modifier == null)
                    modifier = FindModifierBySource(modifiers, upgrade.id, normalizedNodeId);
                if (modifier == null || modifier.scope == null)
                    continue;

                var scopeKind = (modifier.scope.kind ?? string.Empty).Trim();
                var scopedNodeId = (modifier.scope.nodeId ?? string.Empty).Trim();
                if (
                    !string.Equals(scopeKind, "node", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(scopedNodeId, normalizedNodeId, StringComparison.Ordinal)
                )
                    continue;

                if (TryGetUpgradeCost(upgrade, out var cost, out var resourceId))
                {
                    costResourceId = resourceId;
                    return Math.Max(0.0, cost);
                }
            }
        }

        return Math.Max(0.0, fallback);
    }

    private static bool HasTag(string[] tags, string targetTag)
    {
        if (tags == null || tags.Length == 0)
            return false;

        for (int i = 0; i < tags.Length; i++)
        {
            if (string.Equals(tags[i]?.Trim(), targetTag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static ModifierEntry FindModifierById(
        IReadOnlyList<ModifierEntry> modifiers,
        string modifierId
    )
    {
        for (int i = 0; i < modifiers.Count; i++)
        {
            var modifier = modifiers[i];
            if (
                modifier != null
                && string.Equals(
                    (modifier.id ?? string.Empty).Trim(),
                    modifierId,
                    StringComparison.Ordinal
                )
            )
                return modifier;
        }

        return null;
    }

    private static ModifierEntry FindModifierBySource(
        IReadOnlyList<ModifierEntry> modifiers,
        string sourceUpgradeId,
        string nodeId
    )
    {
        var normalizedSource = (sourceUpgradeId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalizedSource))
            return null;

        for (int i = 0; i < modifiers.Count; i++)
        {
            var modifier = modifiers[i];
            if (modifier?.scope == null)
                continue;

            if (
                !string.Equals(
                    (modifier.source ?? string.Empty).Trim(),
                    normalizedSource,
                    StringComparison.Ordinal
                )
            )
                continue;

            if (
                string.Equals(
                    (modifier.scope.kind ?? string.Empty).Trim(),
                    "node",
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(
                    (modifier.scope.nodeId ?? string.Empty).Trim(),
                    nodeId,
                    StringComparison.Ordinal
                )
            )
                return modifier;
        }

        return null;
    }

    private static bool TryGetUpgradeCost(
        UpgradeEntry upgrade,
        out double cost,
        out string resourceId
    )
    {
        cost = 0.0;
        resourceId = "currencySoft";
        if (upgrade == null)
            return false;

        if (upgrade.cost == null || upgrade.cost.Length == 0 || upgrade.cost[0] == null)
            return false;

        resourceId = (upgrade.cost[0].resource ?? string.Empty).Trim();
        var amount = (upgrade.cost[0].amount ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(amount))
            return false;

        if (
            double.TryParse(
                amount,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed
            )
        )
        {
            cost = parsed;
            return true;
        }

        // Fallback to current culture for hand-authored definitions.
        if (double.TryParse(amount, out parsed))
        {
            cost = parsed;
            return true;
        }

        return false;
    }

    private static void LoadGeneratorState(
        GameData data,
        string id,
        NodeInstanceDefinition nodeInstance,
        out bool isOwned,
        out bool isAutomated,
        out int level
    )
    {
        isOwned = nodeInstance?.initialState?.enabled ?? false;
        isAutomated = false;
        level = Math.Max(0, nodeInstance?.initialState?.level ?? 0);

        if (data != null && data.Generators != null)
        {
            var savedGen = data.Generators.Find(g => g != null && g.Id == id);
            if (savedGen != null)
            {
                isOwned = savedGen.IsOwned;
                isAutomated = savedGen.IsAutomated;
                level = savedGen.Level;
            }
        }

        // Normalize: automation implies ownership; ownership implies at least level 1
        if (isAutomated)
            isOwned = true;
        if (isOwned && level < 1)
            level = 1;
        if (!isOwned)
            level = 0;
    }

    private void WireGeneratorPersistence(string id, GeneratorService service)
    {
        Observable
            .CombineLatest(
                service.Level.DistinctUntilChanged(),
                service.IsOwned.DistinctUntilChanged(),
                service.IsAutomated.DistinctUntilChanged(),
                (lvl, owned, automated) => (lvl, owned, automated)
            )
            .Subscribe(state =>
            {
                // Update in-memory save snapshot (facts)
                saveService.SetGeneratorState(id, state.lvl, state.owned, state.automated);

                // Persist upgrade purchases alongside generator state
                upgradeService.SaveInto(saveService.Data);

                // Request a debounced save
                saveService.RequestSave();
            })
            .AddTo(disposables);
    }
}
