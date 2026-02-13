using System;
using System.Collections.Generic;
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
            Debug.LogError("GeneratorListComposer: No node instances found in GameDefinitionService.");
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

        var service = new GeneratorService(model, generatorDefinition, walletService, tickService);

        var generatorViewModel = new GeneratorViewModel(model, generatorDefinition, service);

        generatorServices.Add(service);
        generatorViewModels.Add(generatorViewModel);

        generatorView.Bind(generatorViewModel, service, walletViewModel);

        // Register by instance id (authoritative runtime id).
        uiService.RegisterGenerator(model.Id, service);
        // Compatibility path: allow lookups by node type id when there is a 1:1 instance mapping.
        if (!string.IsNullOrWhiteSpace(nodeTypeId))
            uiService.RegisterGenerator(nodeTypeId.Trim(), service);

        WireGeneratorPersistence(id, service);
    }

    private static GeneratorDefinition CreateRuntimeDefinition(
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
        var output = nodeDef?.outputs != null && nodeDef.outputs.Count > 0 ? nodeDef.outputs[0] : null;
        var payout = output?.basePayout ?? 0.0;
        var perSecond = output?.basePerSecond ?? 0.0;
        definition.BaseOutputPerCycle = payout > 0 ? payout : perSecond * definition.BaseCycleDurationSeconds;

        definition.BaseLevelCost = Math.Max(0.0, nodeDef?.leveling?.priceCurve?.basePrice ?? 0.0);
        definition.LevelCostGrowth = Math.Max(1.0, nodeDef?.leveling?.priceCurve?.growth ?? 1.0);

        // No explicit automation cost in node schema yet; keep existing default.
        definition.AutomationCost = Math.Max(0.0, definition.AutomationCost);

        return definition;
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
