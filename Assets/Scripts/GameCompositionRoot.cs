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
        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        var generatorDefinitions = GetGeneratorDefinitions(maxCount: 3);
        if (generatorDefinitions.Count == 0)
        {
            Debug.LogError("GameCompositionRoot: No GeneratorDefinitions could be resolved.", this);
            enabled = false;
            return;
        }

        InitializeCoreServices(out var modalService);
        if (!enabled)
            return;

        BindSceneUi(modalService);

        tickService = new TickService(TimeSpan.FromMilliseconds(100));

        // Load saved data once for generator initialization (WalletService already loads currency).
        var data = SaveSystem.LoadGame();
        upgradeService.LoadFrom(data);

        CreateGenerators(generatorDefinitions, data);

        // Apply any saved upgrades (generators must be registered before this).
        upgradeService.ApplyAllPurchased();
    }

    private bool ValidateReferences()
    {
        if (generatorDatabase == null)
        {
            Debug.LogError("GameCompositionRoot: GeneratorDatabase is not assigned in the inspector.", this);
            return false;
        }

        if (generatorDatabase.Generators == null || generatorDatabase.Generators.Count == 0)
        {
            Debug.LogError("GameCompositionRoot: GeneratorDatabase has no GeneratorDefinitions assigned.", this);
            return false;
        }

        if (generatorUIRootPrefab == null)
        {
            Debug.LogError("GameCompositionRoot: Generator UI root prefab is not assigned in the inspector.", this);
            return false;
        }

        if (generatorUIContainer == null)
            generatorUIContainer = transform;

        if (uiService == null)
        {
            Debug.LogError("GameCompositionRoot: UiServiceRegistry is not assigned in the inspector.", this);
            return false;
        }

        if (upgradeDatabase == null)
        {
            Debug.LogError("GameCompositionRoot: UpgradeDatabase is not assigned in the inspector.", this);
            return false;
        }

        if (modalManager == null)
        {
            Debug.LogError("GameCompositionRoot: ModalManager is not assigned in the inspector.", this);
            return false;
        }

        if (uiRoot == null)
        {
            Debug.LogError("GameCompositionRoot: UiCompositionRoot is not assigned in the inspector.", this);
            return false;
        }

        return true;
    }

    private List<GeneratorDefinition> GetGeneratorDefinitions(int maxCount)
    {
        var list = new List<GeneratorDefinition>(maxCount);

        foreach (var def in generatorDatabase.Generators)
        {
            if (def == null) continue;

            list.Add(def);
            if (list.Count >= maxCount)
                break;
        }

        return list;
    }

    private void InitializeCoreServices(out ModalService modalService)
    {
        walletService = new WalletService();
        walletViewModel = new WalletViewModel(walletService);

        // Scene-bound registry that exposes runtime services to UI.
        uiService.Initialize(walletService);

        if (uiService is not IGeneratorResolver generatorResolver)
        {
            Debug.LogError("GameCompositionRoot: UiServiceRegistry must implement IGeneratorResolver for UpgradeService.", this);
            enabled = false;
            modalService = null;
            return;
        }

        upgradeService = new UpgradeService(upgradeDatabase, walletService, generatorResolver);

        // ModalManager needs the UpgradeService for modals like Upgrades.
        modalManager.Initialize(upgradeService);

        // Domain-facing modal API (intent-based)
        modalService = new ModalService(modalManager);
    }

    private void BindSceneUi(ModalService modalService)
    {
        var uiCtx = new UiBindingsContext(
            modalService,
            uiService,
            upgradeService,
            walletService,
            walletViewModel
        );

        uiRoot.Bind(uiCtx);
    }

    private void CreateGenerators(List<GeneratorDefinition> generatorDefinitions, GameData data)
    {
        for (int i = 0; i < generatorDefinitions.Count; i++)
        {
            var generatorDefinition = generatorDefinitions[i];

            var generatorUI = Instantiate(generatorUIRootPrefab, generatorUIContainer);
            generatorUI.name = $"Generator_{generatorDefinition.Id}";

            var generatorView = generatorUI.GetComponent<GeneratorView>();
            if (generatorView == null)
            {
                Debug.LogError($"GameCompositionRoot: Generator UI root prefab is missing a GeneratorView (def '{generatorDefinition.Id}').", generatorUI);
                continue;
            }

            CreateSingleGenerator(generatorDefinition, generatorView, data);
        }
    }

    private void CreateSingleGenerator(GeneratorDefinition generatorDefinition, GeneratorView generatorView, GameData data)
    {
        string id = generatorDefinition.Id;

        LoadGeneratorState(data, id, out bool isOwned, out bool isAutomated, out int level);

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

        WireGeneratorPersistence(id, service);
    }

    private static void LoadGeneratorState(GameData data, string id, out bool isOwned, out bool isAutomated, out int level)
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
        if (isAutomated) isOwned = true;
        if (isOwned && level < 1) level = 1;
        if (!isOwned) level = 0;
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
                upgradeService.SaveInto(data);

                SaveSystem.SaveGame(data);
            })
            .AddTo(disposables);
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