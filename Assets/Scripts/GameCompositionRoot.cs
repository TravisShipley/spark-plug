/*
 * GameCompositionRoot
 * -------------------
 * Scene-level composition root responsible for wiring together core domain
 * services, models, view-models, and UI bindings for the game.
 *
 * Responsibilities:
 * - Validate required scene references and configuration
 * - Initialize core services (Wallet, Tick, Upgrade, Screen)
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
    [Header("UI Composition")]
    [SerializeField]
    private UiCompositionRoot uiRoot;

    [SerializeField]
    private UiServiceRegistry uiService;

    [Header("Managers")]
    [SerializeField]
    private UiScreenManager uiScreenManager;

    [Header("Generator UI")]
    [SerializeField]
    private GameObject generatorUIRootPrefab;

    [SerializeField]
    private Transform generatorUIContainer;

    private WalletService walletService;
    private UpgradeService upgradeService;
    private UpgradeListBuilder upgradeListBuilder;
    private UpgradesScreenViewModel upgradesScreenViewModel;
    private ModifierService modifierService;
    private MilestoneService milestoneService;
    private GameDefinitionService gameDefinitionService;
    private TickService tickService;
    private WalletViewModel walletViewModel;
    private SaveService saveService;

    // Timing
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

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

        // SaveService owns an in-memory snapshot.
        saveService = new SaveService();
        saveService.Load();

        InitializeCoreServices(out var uiScreenService);
        if (!enabled)
            return;

        if (gameDefinitionService.NodeInstances == null || gameDefinitionService.NodeInstances.Count == 0)
        {
            Debug.LogError("GameCompositionRoot: No node instances could be resolved.", this);
            enabled = false;
            return;
        }

        LoadSaveState();

        BindSceneUi(uiScreenService);

        var generatorComposer = new GeneratorListComposer(
            generatorUIRootPrefab,
            generatorUIContainer,
            walletService,
            walletViewModel,
            tickService,
            modifierService,
            uiService,
            saveService,
            upgradeService,
            gameDefinitionService,
            disposables,
            generatorModels,
            generatorServices,
            generatorViewModels
        );

        generatorComposer.Compose();

        // Apply any saved upgrades (generators must be registered before this).
        upgradeService.ApplyAllPurchased();

        // Subscribe milestone progression after generators are composed.
        milestoneService = new MilestoneService(
            gameDefinitionService,
            generatorServices,
            saveService,
            modifierService
        );
    }

    private void InitializeCoreServices(out UiScreenService uiScreenService)
    {
        if (saveService == null)
            throw new InvalidOperationException(
                "GameCompositionRoot: SaveService must be created and loaded before InitializeCoreServices."
            );

        // Load content-driven definitions and build catalogs first.
        gameDefinitionService = new GameDefinitionService();

        walletService = new WalletService(saveService, gameDefinitionService.ResourceCatalog);
        walletViewModel = new WalletViewModel(walletService);

        // Time source for simulation
        tickService = new TickService(TickInterval);

        // Scene-bound registry that exposes runtime services to UI.
        uiService.Initialize(walletService);

        // Construct UpgradeService with the authoritative UpgradeCatalog
        upgradeService = new UpgradeService(
            gameDefinitionService.Catalog,
            walletService,
            saveService,
            gameDefinitionService.Modifiers
        );
        modifierService = new ModifierService(
            gameDefinitionService.Modifiers,
            gameDefinitionService.Catalog,
            gameDefinitionService.NodeCatalog,
            gameDefinitionService.NodeInstanceCatalog,
            upgradeService,
            saveService,
            gameDefinitionService.Milestones
        );
        walletService.SetModifierService(modifierService);

        // UiScreenManager needs the UpgradeService for screens like Upgrades.
        uiScreenManager.Initialize(upgradeService);
        // Provide the content-driven catalog to screens so they can render upgrades.
        uiScreenManager.UpgradeCatalog = gameDefinitionService.Catalog;
        // Also expose the full GameDefinitionService for screens that need richer access.
        uiScreenManager.GameDefinitionService = gameDefinitionService;
        upgradeListBuilder = new UpgradeListBuilder(
            gameDefinitionService.Catalog,
            upgradeService,
            gameDefinitionService
        );
        upgradesScreenViewModel = new UpgradesScreenViewModel(upgradeListBuilder);
        uiScreenManager.UpgradesScreenViewModel = upgradesScreenViewModel;

        // Domain-facing screen API (intent-based)
        uiScreenService = new UiScreenService(uiScreenManager);
    }

    private void LoadSaveState()
    {
        if (saveService == null)
            throw new InvalidOperationException(
                "GameCompositionRoot: SaveService is null in LoadSaveState."
            );

        if (upgradeService == null)
            throw new InvalidOperationException(
                "GameCompositionRoot: UpgradeService is null in LoadSaveState."
            );

        // Load saved upgrade purchase facts. (WalletService loads currency from SaveService in its constructor.)
        upgradeService.LoadFrom(saveService.Data);
        modifierService.RebuildActiveModifiers();
    }

    private void BindSceneUi(UiScreenService uiScreenService)
    {
        var uiCtx = new UiBindingsContext(
            uiScreenService,
            uiService,
            upgradeService,
            walletService,
            walletViewModel
        );

        uiRoot.Bind(uiCtx);
    }

    private bool ValidateReferences()
    {
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

        if (uiScreenManager == null)
        {
            Debug.LogError(
                "GameCompositionRoot: UiScreenManager is not assigned in the inspector.",
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
        saveService?.Dispose();

        disposables.Dispose();

        // Dispose simulation/services first (they may publish into viewmodels/services)
        // automationService?.Dispose();
        foreach (var s in generatorServices)
            s?.Dispose();
        milestoneService?.Dispose();
        modifierService?.Dispose();

        // Dispose time source after consumers
        tickService?.Dispose();

        // Dispose viewmodels after services (they may be subscribed to service state)
        foreach (var generatorViewModel in generatorViewModels)
            generatorViewModel?.Dispose();
        upgradesScreenViewModel?.Dispose();
        walletViewModel?.Dispose();

        // Dispose core state last
        upgradeService?.Dispose();
        walletService?.Dispose();
    }
}
