using UnityEngine;
using System;
using UniRx;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class GameCompositionRoot : MonoBehaviour
{
    [Header("Databases")]
    [SerializeField] private GeneratorDatabase generatorDatabase;
    [SerializeField] private UpgradeDatabase upgradeDatabase;

    [Header("UI Composition")]
    [SerializeField] private UiCompositionRoot uiRoot;
    [SerializeField] private UiServiceRegistry uiService;

    [Header("Managers")]
    [SerializeField] private ModalManager modalManager;
    
    [Header("Generator UI")]
    [SerializeField] private GameObject generatorUIRootPrefab;
    [SerializeField] private Transform generatorUIContainer;

    private WalletService walletService;
    private UpgradeService upgradeService;
    private TickService tickService;
    private WalletViewModel walletViewModel;
    private readonly List<GeneratorModel> generatorModels = new();
    private readonly List<GeneratorService> generatorServices = new();
    private readonly List<GeneratorViewModel> generatorViewModels = new();
    private readonly CompositeDisposable disposables = new CompositeDisposable();

    private void Awake()
    {
        if (generatorDatabase == null)
        {
            Debug.LogError("GameController: GeneratorDatabase is not assigned in the inspector.");
            enabled = false;
            return;
        }

        // Build the list of generator definitions to instantiate (up to 3 by default)
        if (generatorDatabase.Generators == null || generatorDatabase.Generators.Count == 0)
        {
            Debug.LogError("GameController: GeneratorDatabase has no GeneratorDefinitions assigned.");
            enabled = false;
            return;
        }

        var generatorDefinitions = new List<GeneratorDefinition>(3);

        foreach (var generatorDefinition in generatorDatabase.Generators)
        {
            if (generatorDefinition == null) continue;

            generatorDefinitions.Add(generatorDefinition);
            if (generatorDefinitions.Count >= 3) break;
        }

        if (generatorDefinitions.Count == 0)
        {
            Debug.LogError("GameController: No GeneratorDefinitions could be resolved.");
            enabled = false;
            return;
        }

        if (generatorUIRootPrefab == null)
        {
            Debug.LogError("GameController: Generator UI root prefab is not assigned in the inspector.");
            enabled = false;
            return;
        }

        if (generatorUIContainer == null)
            generatorUIContainer = transform;

        // create and wire the wallet service -> viewmodel
        walletService = new WalletService();
        walletViewModel = new WalletViewModel(walletService);

        // UI service registry is required for UI-driven systems (eg: modals resolving services)
        if (uiService == null)
        {
            Debug.LogError("GameController: UiServiceRegistry is not assigned in the inspector.", this);
            enabled = false;
            return;
        }

        uiService.Initialize(walletService);

        if (upgradeDatabase == null)
        {
            Debug.LogError("GameController: UpgradeDatabase is not assigned in the inspector.", this);
            enabled = false;
            return;
        }

        if (uiService is not IGeneratorResolver generatorResolver)
        {
            Debug.LogError("GameController: UiServiceRegistry must implement IGeneratorResolver for UpgradeService.", this);
            enabled = false;
            return;
        }

        upgradeService = new UpgradeService(upgradeDatabase, walletService, generatorResolver);

        if (modalManager == null)
        {
            Debug.LogError("GameController: ModalManager is not assigned in the inspector.", this);
            enabled = false;
            return;
        }
        // ModalManager needs the UpgradeService for modals like Upgrades.
        modalManager.Initialize(upgradeService);

        // Domain-facing modal API (intent-based)
        var modalService = new ModalService(modalManager);

        if (uiRoot == null)
        {
            Debug.LogError("GameController: UiCompositionRoot is not assigned in the inspector.", this);
            enabled = false;
            return;
        }

        var uiCtx = new UiBindingsContext(
            modalService,
            uiService,
            upgradeService,
            walletService,
            walletViewModel
        );

        uiRoot.Bind(uiCtx);

        tickService = new TickService(TimeSpan.FromMilliseconds(100));

        // Load saved data once for generator initialization (WalletService already loads currency).
        var data = SaveSystem.LoadGame();
        upgradeService.LoadFrom(data);

        // Instantiate generator UI prefabs and wire up GeneratorView and AutomateButtonView for each generator
        for (int i = 0; i < generatorDefinitions.Count; i++)
        {
            var generatorDefinition = generatorDefinitions[i];

            // Create UI instance for this generator
            var generatorUI = Instantiate(generatorUIRootPrefab, generatorUIContainer);
            generatorUI.name = $"Generator_{generatorDefinition.Id}";

            var generatorView = generatorUI.GetComponent<GeneratorView>();
            if (generatorView == null)
            {
                Debug.LogError($"GameController: Generator UI root prefab is missing a GeneratorView (def '{generatorDefinition.Id}').", generatorUI);
                continue;
            }

            string id = generatorDefinition.Id;

            bool isOwned = false;
            bool isAutomated = false;
            int level = 0;

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
            if (isAutomated) isOwned = true;
            if (isOwned && level < 1) level = 1;
            if (!isOwned) level = 0;

            var model = new GeneratorModel
            {
                Id = id,
                Level = level,
                IsOwned = isOwned,
                IsAutomated = isAutomated,
                CycleElapsedSeconds = 0
            };

            generatorModels.Add(model);

            var service = new GeneratorService(
                model,
                generatorDefinition,
                walletService,
                tickService
            );

            var generatorViewModel = new GeneratorViewModel(model, generatorDefinition, service);

            generatorServices.Add(service);
            generatorViewModels.Add(generatorViewModel);

            generatorView.Bind(generatorViewModel, service, walletViewModel);

            // Register each generator and service with the UiServiceRegistry
            uiService.RegisterGenerator(model.Id, service);

            // Persist generator state whenever it changes
            Observable
                .CombineLatest(
                    service.Level.DistinctUntilChanged(),
                    service.IsOwned.DistinctUntilChanged(),
                    service.IsAutomated.DistinctUntilChanged(),
                    (lvl, owned, automated) => (lvl, owned, automated)
                )
                .Subscribe(state =>
                {
                    var data = SaveSystem.LoadGame() ?? new GameData();
                    data.Generators ??= new List<GameData.GeneratorStateData>();

                    var entry = data.Generators.Find(g => g != null && g.Id == id);
                    if (entry == null)
                    {
                        entry = new GameData.GeneratorStateData { Id = id };
                        data.Generators.Add(entry);
                    }

                    entry.IsOwned = state.owned;
                    entry.IsAutomated = state.automated;
                    entry.Level = state.lvl;

                    // Persist upgrade purchases alongside generator state
                    upgradeService?.SaveInto(data);

                    SaveSystem.SaveGame(data);
                })
                .AddTo(disposables);
        }

        // Apply any saved upgrades
        upgradeService.ApplyAllPurchased();
    }

    private void OnEnable()
    {
        SaveSystem.OnSaveReset += OnSaveReset;
    }

    private void OnDisable()
    {
        SaveSystem.OnSaveReset -= OnSaveReset;
    }

    private void OnSaveReset(GameData _)
    {
        // For testing: a full scene reload guarantees all services, models, and views reset cleanly
        // without needing every ViewModel/View to support re-initialization.
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void OnDestroy()
    {
        disposables.Dispose();

        // Dispose simulation/services first (they may publish into viewmodels/services)
        // automationService?.Dispose();
        foreach (var s in generatorServices) s?.Dispose();

        // Dispose time source after consumers
        tickService?.Dispose();

        // Dispose viewmodels after services (they may be subscribed to service state)
        foreach (var generatorViewModel in generatorViewModels) generatorViewModel?.Dispose();
        walletViewModel?.Dispose();

        // Dispose core state last
        upgradeService?.Dispose();
        walletService?.Dispose();
    }
}