using System.Collections.Generic;
using UniRx;
using UnityEngine;

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
        this.disposables = disposables;
        this.generatorModels = generatorModels;
        this.generatorServices = generatorServices;
        this.generatorViewModels = generatorViewModels;
    }

    public void Compose(List<GeneratorDefinition> generatorDefinitions)
    {
        for (int i = 0; i < generatorDefinitions.Count; i++)
        {
            var generatorDefinition = generatorDefinitions[i];

            var generatorUI = Object.Instantiate(generatorUIRootPrefab, generatorUIContainer);
            generatorUI.name = $"Generator_{generatorDefinition.Id}";

            var generatorView = generatorUI.GetComponent<GeneratorView>();
            if (generatorView == null)
            {
                Debug.LogError(
                    $"GameCompositionRoot: Generator UI root prefab is missing a GeneratorView (def '{generatorDefinition.Id}').",
                    generatorUI
                );
                continue;
            }

            ComposeSingle(generatorDefinition, generatorView);
        }
    }

    private void ComposeSingle(GeneratorDefinition generatorDefinition, GeneratorView generatorView)
    {
        string id = generatorDefinition.Id;

        LoadGeneratorState(saveService.Data, id, out bool isOwned, out bool isAutomated, out int level);

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

        // Register each generator and service with the UiServiceRegistry
        uiService.RegisterGenerator(model.Id, service);

        WireGeneratorPersistence(id, service);
    }

    private static void LoadGeneratorState(
        GameData data,
        string id,
        out bool isOwned,
        out bool isAutomated,
        out int level
    )
    {
        isOwned = false;
        isAutomated = false;
        level = 0;

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
