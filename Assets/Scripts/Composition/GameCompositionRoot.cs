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
using System.Collections;
using System.Collections.Generic;
using UniRx;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-1000)]
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

    [Header("Debug UI")]
    [SerializeField]
    private ClearSaveButton clearSaveButton;

    private WalletService walletService;
    private UpgradeService upgradeService;
    private UpgradeListBuilder upgradeListBuilder;
    private UpgradesScreenViewModel upgradesScreenViewModel;
    private ManagersScreenViewModel managersScreenViewModel;
    private AdBoostScreenViewModel adBoostScreenViewModel;
    private PrestigeScreenViewModel prestigeScreenViewModel;
    private BuffService buffService;
    private BuyModeService buyModeService;
    private ModifierService modifierService;
    private MilestoneService milestoneService;
    private TriggerService triggerService;
    private UnlockService unlockService;
    private PrestigeService prestigeService;
    private OfflineProgressCalculator offlineProgressCalculator;
    private TimeWarpService timeWarpService;
    private GameDefinitionService gameDefinitionService;
    private ComputedVarService computedVarService;
    private TickService tickService;
    private WalletViewModel walletViewModel;
    private SaveService saveService;
    private GameEventStream gameEventStream;

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);
    private readonly List<GeneratorModel> generatorModels = new();
    private readonly List<GeneratorService> generatorServices = new();
    private readonly List<GeneratorViewModel> generatorViewModels = new();
    private readonly CompositeDisposable disposables = new CompositeDisposable();
    private bool referencesValidated;
    private bool bootStarted;
    private GameSessionConfigAsset activeSessionConfigAsset;
    private SparkPlugRuntimeConfig runtimeConfig;

    private void Awake()
    {
        referencesValidated = ValidateReferences();
        if (!referencesValidated)
        {
            enabled = false;
            return;
        }
    }

    private void Start()
    {
        if (!enabled || bootStarted)
            return;

        if (FindAnyObjectByType<GameSessionBootstrapper>() != null)
            return;

        StartCoroutine(BootstrapLegacyAsync());
    }

    public void BeginBootstrap(
        SparkPlugRuntimeConfig runtimeConfig,
        GameSessionConfigAsset sessionConfigAsset = null
    )
    {
        if (runtimeConfig == null)
            throw new ArgumentNullException(nameof(runtimeConfig));

        if (!referencesValidated)
        {
            referencesValidated = ValidateReferences();
            if (!referencesValidated)
            {
                enabled = false;
                return;
            }
        }

        if (bootStarted)
        {
            Debug.LogWarning("GameCompositionRoot: BeginBootstrap called more than once.", this);
            return;
        }

        bootStarted = true;
        this.runtimeConfig = runtimeConfig;
        activeSessionConfigAsset = sessionConfigAsset;
        saveService = new SaveService(
            SparkPlugSaveKey.Compose(runtimeConfig.SessionId, runtimeConfig.SaveSlotId)
        );

        if (runtimeConfig.VerboseLogging)
        {
            Debug.Log(
                $"GameCompositionRoot: Starting session '{runtimeConfig.SessionId}' with save key '{saveService.SaveKey}'.",
                this
            );
        }

        try
        {
            BootstrapRuntime(runtimeConfig);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
            enabled = false;
        }
    }

    private IEnumerator BootstrapLegacyAsync()
    {
        GameDefinition loadedDefinition = null;
        Exception loadException = null;

        yield return StartCoroutine(
            GameDefinitionLoader.LoadFromAddressableAsync(
                gd => loadedDefinition = gd,
                ex => loadException = ex
            )
        );

        if (loadException != null)
        {
            Debug.LogException(loadException, this);
            enabled = false;
            yield break;
        }

        if (loadedDefinition == null)
        {
            Debug.LogError(
                "GameCompositionRoot: GameDefinitionLoader completed without data.",
                this
            );
            enabled = false;
            yield break;
        }

        BeginBootstrap(
            new SparkPlugRuntimeConfig(
                SparkPlugSaveKey.DefaultSessionId,
                "Spark Plug",
                SparkPlugSaveKey.DefaultSaveSlotId,
                resetSaveOnBoot: false,
                verboseLogging: false,
                loadedDefinition
            )
        );
    }

    private void BootstrapRuntime(SparkPlugRuntimeConfig runtimeConfig)
    {
        InitializeCoreServices(runtimeConfig, out var uiScreenService);

        if (gameDefinitionService.NodeInstances.Count == 0)
        {
            Debug.LogError("GameCompositionRoot: No node instances could be resolved.", this);
            enabled = false;
            return;
        }

        LoadSaveState();

        unlockService = new UnlockService(
            gameDefinitionService,
            uiService,
            upgradeService,
            saveService
        );
        unlockService.LoadUnlockedIds(saveService.Data?.UnlockedNodeInstanceIds);

        BindSceneUi(uiScreenService);
        BindDebugUi();
        TryShowOfflineEarnings(uiScreenService);
        gameEventStream
            .TimeWarpCompleted.Subscribe(evt => ShowTimeWarpResults(uiScreenService, evt.result))
            .AddTo(disposables);

        var generatorComposer = new GeneratorListComposer(
            generatorUIRootPrefab,
            generatorUIContainer,
            walletService,
            walletViewModel,
            tickService,
            modifierService,
            gameEventStream,
            buffService,
            buyModeService,
            uiService,
            saveService,
            gameDefinitionService,
            computedVarService,
            unlockService,
            disposables,
            generatorModels,
            generatorServices,
            generatorViewModels
        );

        generatorComposer.Compose();
        upgradeService.ApplyAllPurchased();

        milestoneService = new MilestoneService(
            gameDefinitionService,
            generatorServices,
            saveService,
            modifierService,
            gameEventStream
        );
        triggerService = new TriggerService(
            gameDefinitionService,
            walletService,
            timeWarpService,
            gameEventStream
        );

        gameEventStream
            .ResetSaveRequested.Subscribe(_ => HandleResetRequested())
            .AddTo(disposables);
    }

    private void InitializeCoreServices(
        SparkPlugRuntimeConfig runtimeConfig,
        out UiScreenService uiScreenService
    )
    {
        if (saveService == null)
        {
            throw new InvalidOperationException(
                "GameCompositionRoot: SaveService must be created before InitializeCoreServices."
            );
        }

        gameDefinitionService = new GameDefinitionService(runtimeConfig.Definition);
        saveService.Load(gameDefinitionService.Definition, runtimeConfig.ResetSaveOnBoot);
        gameEventStream = new GameEventStream();

        walletService = new WalletService(
            saveService,
            gameDefinitionService.ResourceCatalog,
            gameEventStream
        );
        walletViewModel = new WalletViewModel(walletService);
        computedVarService = new ComputedVarService(
            gameDefinitionService,
            saveService,
            walletService
        );

        tickService = new TickService(TickInterval);

        uiService.Initialize(walletService);

        upgradeService = new UpgradeService(
            gameDefinitionService.UpgradeCatalog,
            walletService,
            saveService,
            gameDefinitionService.Modifiers
        );
        prestigeService = new PrestigeService(
            gameDefinitionService,
            saveService,
            walletService,
            gameEventStream
        );
        modifierService = new ModifierService(
            gameDefinitionService.Modifiers,
            gameDefinitionService.UpgradeCatalog,
            gameDefinitionService.NodeCatalog,
            gameDefinitionService.NodeInstanceCatalog,
            upgradeService,
            saveService,
            gameDefinitionService.Milestones,
            prestigeService
        );
        buffService = new BuffService(
            saveService,
            gameDefinitionService.BuffCatalog,
            modifierService,
            tickService
        );
        buyModeService = new BuyModeService(gameDefinitionService.BuyModeCatalog);
        uiService.RegisterBuffService(buffService);
        uiService.RegisterBuyModeService(buyModeService);
        walletService.SetModifierService(modifierService);
        offlineProgressCalculator = new OfflineProgressCalculator(
            gameDefinitionService,
            modifierService
        );
        timeWarpService = new TimeWarpService(
            offlineProgressCalculator,
            saveService,
            walletService,
            gameEventStream
        );

        uiScreenManager.Initialize(upgradeService);
        uiScreenManager.UpgradeCatalog = gameDefinitionService.UpgradeCatalog;
        uiScreenManager.GameDefinitionService = gameDefinitionService;
        upgradeListBuilder = new UpgradeListBuilder(
            gameDefinitionService.UpgradeCatalog,
            upgradeService,
            gameDefinitionService
        );
        upgradesScreenViewModel = new UpgradesScreenViewModel(upgradeListBuilder);
        managersScreenViewModel = new ManagersScreenViewModel(upgradeListBuilder);
        adBoostScreenViewModel = null;
        if (
            gameDefinitionService.BuffCatalog.TryGet(AdBoostScreenViewModel.CanonicalBuffId, out _)
        )
        {
            adBoostScreenViewModel = new AdBoostScreenViewModel(
                buffService,
                gameDefinitionService.BuffCatalog,
                CloseTopScreen
            );
        }
        prestigeScreenViewModel = new PrestigeScreenViewModel(prestigeService, CloseTopScreen);
        uiScreenManager.UpgradesScreenViewModel = upgradesScreenViewModel;
        uiScreenManager.ManagersScreenViewModel = managersScreenViewModel;
        uiScreenManager.AdBoostScreenViewModel = adBoostScreenViewModel;
        uiScreenManager.PrestigeScreenViewModel = prestigeScreenViewModel;

        uiScreenService = new UiScreenService(uiScreenManager, walletService);
    }

    private void LoadSaveState()
    {
        if (saveService == null)
        {
            throw new InvalidOperationException(
                "GameCompositionRoot: SaveService is null in LoadSaveState."
            );
        }

        if (upgradeService == null)
        {
            throw new InvalidOperationException(
                "GameCompositionRoot: UpgradeService is null in LoadSaveState."
            );
        }

        upgradeService.LoadFrom(saveService.Data);
        modifierService.RebuildActiveModifiers();
    }

    private void BindSceneUi(UiScreenService uiScreenService)
    {
        var uiCtx = new UiBindingsContext(
            uiScreenService,
            uiService,
            upgradeService,
            buyModeService,
            timeWarpService,
            walletService,
            walletViewModel
        );

        uiRoot.Bind(uiCtx);
    }

    private void BindDebugUi()
    {
        if (clearSaveButton == null)
            return;

        clearSaveButton.Bind(gameEventStream);
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

    private void OnDestroy()
    {
        UpdateLastSeenTimestamp(saveImmediately: false);

        saveService?.Dispose();
        disposables.Dispose();

        foreach (var s in generatorServices)
            s?.Dispose();
        milestoneService?.Dispose();
        triggerService?.Dispose();
        buffService?.Dispose();
        buyModeService?.Dispose();
        unlockService?.Dispose();
        modifierService?.Dispose();

        tickService?.Dispose();

        foreach (var generatorViewModel in generatorViewModels)
            generatorViewModel?.Dispose();
        upgradesScreenViewModel?.Dispose();
        managersScreenViewModel?.Dispose();
        adBoostScreenViewModel?.Dispose();
        prestigeScreenViewModel?.Dispose();
        walletViewModel?.Dispose();

        upgradeService?.Dispose();
        prestigeService?.Dispose();
        walletService?.Dispose();
        gameEventStream?.Dispose();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (!pauseStatus)
            return;

        UpdateLastSeenTimestamp(saveImmediately: true);
    }

    private void OnApplicationQuit()
    {
        UpdateLastSeenTimestamp(saveImmediately: true);
    }

    private void HandleResetRequested()
    {
        if (saveService == null || gameDefinitionService?.Definition == null)
            return;

        if (runtimeConfig != null && runtimeConfig.VerboseLogging)
        {
            Debug.Log(
                $"GameCompositionRoot: Reset requested for session '{runtimeConfig.SessionId}'.",
                this
            );
        }

        if (!saveService.ConsumePendingScopedResetReload())
            saveService.Reset(gameDefinitionService.Definition);
        else
            saveService.SaveNow();

        if (activeSessionConfigAsset != null)
            SparkPlugBootContext.SetPendingSession(activeSessionConfigAsset);

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void TryShowOfflineEarnings(UiScreenService uiScreenService)
    {
        if (
            uiScreenService == null
            || saveService == null
            || offlineProgressCalculator == null
            || saveService.Data == null
        )
        {
            return;
        }

        var now = GetCurrentUnixSeconds();
        var lastSeen = saveService.LastSeenUnixSeconds;
        if (lastSeen <= 0 || lastSeen > now)
        {
            saveService.SetLastSeenUnixSeconds(now, requestSave: true);
            return;
        }

        var secondsAway = Math.Max(0, now - lastSeen);
        var result = offlineProgressCalculator.Calculate(secondsAway, saveService.Data);

        if (result != null && result.HasGeneratorStateChanges())
            saveService.ApplyOfflineSessionResult(result, requestSave: false);

        saveService.SetLastSeenUnixSeconds(now, requestSave: true);

        if (result != null && result.HasMeaningfulGain())
            uiScreenService.ShowOfflineEarnings(result);
    }

    private static void ShowTimeWarpResults(
        UiScreenService uiScreenService,
        OfflineSessionResult result
    )
    {
        if (uiScreenService == null)
            return;

        uiScreenService.Show("TIME_WARP_RESULTS", new TimeWarpResultsScreenViewModel(result));
    }

    private void UpdateLastSeenTimestamp(bool saveImmediately)
    {
        if (saveService == null)
            return;

        saveService.SetLastSeenUnixSeconds(GetCurrentUnixSeconds(), requestSave: !saveImmediately);
        if (saveImmediately)
            saveService.SaveNow();
    }

    private static long GetCurrentUnixSeconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private void CloseTopScreen()
    {
        uiScreenManager?.CloseTop();
    }

    /*
    Prestige smoke checklist:
    - Earn currencySoft and verify lifetime earnings increase.
    - Open Prestige and confirm preview gain is > 0.
    - Perform prestige and confirm:
      nodes + upgrades + milestones/unlocks reset,
      currencySoft resets,
      currencyHard is preserved,
      currencyMeta increases.
    - After reload, verify currencySoft income is higher from prestige multiplier.
    */
}
