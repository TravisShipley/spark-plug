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
    private readonly GameData gameData;
    private readonly UpgradeService upgradeService;
    private readonly Subject<Unit> saveRequests;
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
        GameData gameData,
        UpgradeService upgradeService,
        Subject<Unit> saveRequests,
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
        this.gameData = gameData;
        this.upgradeService = upgradeService;
        this.saveRequests = saveRequests;
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

        LoadGeneratorState(gameData, id, out bool isOwned, out bool isAutomated, out int level);

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
                gameData.Generators ??= new List<GameData.GeneratorStateData>();

                var entry = gameData.Generators.Find(g => g != null && g.Id == id);
                if (entry == null)
                {
                    entry = new GameData.GeneratorStateData { Id = id };
                    gameData.Generators.Add(entry);
                }

                entry.IsOwned = state.owned;
                entry.IsAutomated = state.automated;
                entry.Level = state.lvl;

                // Persist upgrade purchases alongside generator state
                upgradeService.SaveInto(gameData);

                // Request a debounced save
                saveRequests.OnNext(Unit.Default);
            })
            .AddTo(disposables);
    }
}
