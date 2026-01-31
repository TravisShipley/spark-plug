/*
 * GameCompositionRoot
 * -------------------
 * Scene-level composition root responsible for wiring together core domain
 * services, models, view-models, and UI bindings for the game.
 *
 * Responsibilities:
 * - Validate required scene references and configuration
 * - Initialize core services (Wallet, Tick, Upgrade, Modal)
 * - Bind runtime services into the UI composition layer
 * - Instantiate and compose Generator models, services, and view-models
 * - Restore persisted game state and apply saved upgrades
 * - Wire reactive persistence for generator state changes
 *
 * Design notes:
 * - This class intentionally performs orchestration only; it contains no
 *   gameplay logic of its own.
 * - Lifetime is scene-bound; a full scene reload is used to guarantee a clean
 *   reset of state on save resets.
 * - Acts as the single entry point for dependency wiring to avoid hidden or
 *   implicit initialization elsewhere.
 */

using System;
using System.Collections.Generic;
using UniRx;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameCompositionRoot : MonoBehaviour
{
    [Header("Databases")]
    [SerializeField]
    private GeneratorDatabase generatorDatabase;

    [SerializeField]
    private UpgradeDatabase upgradeDatabase;

    [Header("UI Composition")]
    [SerializeField]
    private UiCompositionRoot uiRoot;

    [SerializeField]
    private UiServiceRegistry uiService;

    [Header("Managers")]
    [SerializeField]
    private ModalManager modalManager;

    [Header("Generator UI")]
    [SerializeField]
    private GameObject generatorUIRootPrefab;

    [SerializeField]
    private Transform generatorUIContainer;

    private WalletService walletService;
    private UpgradeService upgradeService;
    private TickService tickService;
    private WalletViewModel walletViewModel;

    private GameData gameData;
    private readonly Subject<Unit> saveRequests = new Subject<Unit>();

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

        // Load saved data once into memory (WalletService already loads currency).
        gameData = SaveSystem.LoadGame() ?? new GameData();
        gameData.Generators ??= new List<GameData.GeneratorStateData>();
        gameData.Upgrades ??= new List<GameData.UpgradeStateData>();

        // Debounced disk writes to avoid load/save churn.
        saveRequests
            .Throttle(TimeSpan.FromMilliseconds(250))
            .Subscribe(_ => SaveSystem.SaveGame(gameData))
            .AddTo(disposables);

        upgradeService.LoadFrom(gameData);

        var generatorComposer = new GeneratorListComposer(
            generatorUIRootPrefab,
            generatorUIContainer,
            walletService,
            walletViewModel,
            tickService,
            uiService,
            gameData,
            upgradeService,
            saveRequests,
            disposables,
            generatorModels,
            generatorServices,
            generatorViewModels
        );

        generatorComposer.Compose(generatorDefinitions);

        // Apply any saved upgrades (generators must be registered before this).
        upgradeService.ApplyAllPurchased();
    }

    private bool ValidateReferences()
    {
        if (generatorDatabase == null)
        {
            Debug.LogError(
                "GameCompositionRoot: GeneratorDatabase is not assigned in the inspector.",
                this
            );
            return false;
        }

        if (generatorDatabase.Generators == null || generatorDatabase.Generators.Count == 0)
        {
            Debug.LogError(
                "GameCompositionRoot: GeneratorDatabase has no GeneratorDefinitions assigned.",
                this
            );
            return false;
        }

        if (generatorUIRootPrefab == null)
        {
            Debug.LogError(
                "GameCompositionRoot: Generator UI root prefab is not assigned in the inspector.",
                this
            );
            return false;
        }

        if (generatorUIContainer == null)
            generatorUIContainer = transform;

        if (uiService == null)
        {
            Debug.LogError(
                "GameCompositionRoot: UiServiceRegistry is not assigned in the inspector.",
                this
            );
            return false;
        }

        if (upgradeDatabase == null)
        {
            Debug.LogError(
                "GameCompositionRoot: UpgradeDatabase is not assigned in the inspector.",
                this
            );
            return false;
        }

        if (modalManager == null)
        {
            Debug.LogError(
                "GameCompositionRoot: ModalManager is not assigned in the inspector.",
                this
            );
            return false;
        }

        if (uiRoot == null)
        {
            Debug.LogError(
                "GameCompositionRoot: UiCompositionRoot is not assigned in the inspector.",
                this
            );
            return false;
        }

        return true;
    }

    private List<GeneratorDefinition> GetGeneratorDefinitions(int maxCount)
    {
        var list = new List<GeneratorDefinition>(maxCount);

        foreach (var def in generatorDatabase.Generators)
        {
            if (def == null)
                continue;

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

        // Time source for simulation
        tickService = new TickService(TimeSpan.FromMilliseconds(100));

        // Scene-bound registry that exposes runtime services to UI.
        uiService.Initialize(walletService);

        if (uiService is not IGeneratorResolver generatorResolver)
        {
            Debug.LogError(
                "GameCompositionRoot: UiServiceRegistry must implement IGeneratorResolver for UpgradeService.",
                this
            );
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
        // Flush any pending save (best effort)
        if (gameData != null)
            SaveSystem.SaveGame(gameData);

        saveRequests.OnCompleted();
        saveRequests.Dispose();

        disposables.Dispose();

        // Dispose simulation/services first (they may publish into viewmodels/services)
        // automationService?.Dispose();
        foreach (var s in generatorServices)
            s?.Dispose();

        // Dispose time source after consumers
        tickService?.Dispose();

        // Dispose viewmodels after services (they may be subscribed to service state)
        foreach (var generatorViewModel in generatorViewModels)
            generatorViewModel?.Dispose();
        walletViewModel?.Dispose();

        // Dispose core state last
        upgradeService?.Dispose();
        walletService?.Dispose();
    }
}
