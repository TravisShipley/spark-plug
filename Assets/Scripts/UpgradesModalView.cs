using UniRx;
using UnityEngine;

public sealed class UpgradesModalView : ModalView
{
    [Header("UI")]
    [SerializeField]
    private Transform listContainer;

    [SerializeField]
    private UpgradeEntryView entryPrefab;

    [Header("Data")]
    // Upgrades are now driven by the content pack -> UpgradeCatalog.
    // Resolve the catalog from ModalManager at runtime.

    private UpgradeService upgradeService;

    private readonly CompositeDisposable disposables = new CompositeDisposable();

    public override void OnBeforeShow(object payload)
    {
        disposables.Clear();
        ClearList();

        // Prefer GameDefinitionService as the authoritative source of upgrades.
        GameDefinitionService gameDefService = null;
        var gdsProp = Manager.GetType().GetProperty("GameDefinitionService");
        if (gdsProp != null)
            gameDefService = gdsProp.GetValue(Manager) as GameDefinitionService;

        UpgradeCatalog upgradeCatalog = null;
        if (gameDefService != null)
            upgradeCatalog = new UpgradeCatalog(gameDefService.Upgrades);
        else
        {
            var catalogProp = Manager.GetType().GetProperty("UpgradeCatalog");
            if (catalogProp != null)
                upgradeCatalog = catalogProp.GetValue(Manager) as UpgradeCatalog;
        }

        if (upgradeCatalog == null)
        {
            Debug.LogError(
                "UpgradesModalView: Could not resolve upgrades source from ModalManager. "
                    + "Expose `GameDefinitionService` or `UpgradeCatalog` on ModalManager and assign it during bootstrap.",
                this
            );
            return;
        }

        if (entryPrefab == null || listContainer == null)
        {
            Debug.LogError(
                "UpgradesModalView: entryPrefab or listContainer is not assigned.",
                this
            );
            return;
        }

        if (Manager == null)
        {
            Debug.LogError(
                "UpgradesModalView: Manager is not set; this modal must be shown via ModalManager.",
                this
            );
            return;
        }

        // Generator lookup is provided by the ModalManager (delegates to UiServiceRegistry).
        var generatorResolver = Manager as IGeneratorResolver;

        // UpgradeService should be exposed by ModalManager via a public property `UpgradeService`.
        upgradeService = null;
        var prop = Manager.GetType().GetProperty("UpgradeService");
        if (prop != null)
            upgradeService = prop.GetValue(Manager) as UpgradeService;

        if (upgradeService == null)
        {
            Debug.LogError(
                "UpgradesModalView: UpgradeService could not be resolved from ModalManager. "
                    + "Expose a public property `public UpgradeService UpgradeService { get; }` on ModalManager and assign it during bootstrap.",
                this
            );
            return;
        }

        if (upgradeService.Wallet == null)
        {
            Debug.LogError(
                "UpgradesModalView: UpgradeService.Wallet is null (did bootstrap initialize it?).",
                this
            );
            return;
        }

        // Show all upgrades in the catalog. Each entry wires itself to the generator specified by upgrade.generatorId.
        foreach (var upgrade in upgradeCatalog.Upgrades)
        {
            if (upgrade == null)
                continue;

            string genId = (upgrade.generatorId ?? string.Empty).Trim();
            GeneratorService generator = null;
            if (!string.IsNullOrEmpty(genId))
            {
                if (generatorResolver == null)
                    continue;

                if (!generatorResolver.TryGetGenerator(genId, out generator) || generator == null)
                    continue;
            }

            var entry = Instantiate(entryPrefab, listContainer);
            entry.name = $"Upgrade_{upgrade.id}";

            var purchasedCount = upgradeService
                .PurchasedCount(upgrade.id)
                .DistinctUntilChanged()
                .ToReadOnlyReactiveProperty()
                .AddTo(disposables);

            entry.Bind(upgrade, generator, upgradeService, purchasedCount);
        }
    }

    private void ClearList()
    {
        if (listContainer == null)
            return;

        for (int i = listContainer.childCount - 1; i >= 0; i--)
        {
            var child = listContainer.GetChild(i);
            if (child != null)
                Destroy(child.gameObject);
        }
    }

    private void OnDestroy()
    {
        disposables.Dispose();
    }
}
