using UniRx;
using UnityEngine;

public sealed class UpgradesScreenView : UiScreenView
{
    [Header("UI")]
    [SerializeField]
    private Transform listContainer;

    [SerializeField]
    private UpgradeEntryView entryPrefab;

    [Header("Data")]
    // Upgrades are now driven by the content pack -> UpgradeCatalog.
    // Resolve the catalog from UiScreenManager at runtime.

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
                "UpgradesScreenView: Could not resolve upgrades source from UiScreenManager. "
                    + "Expose `GameDefinitionService` or `UpgradeCatalog` on UiScreenManager and assign it during bootstrap.",
                this
            );
            return;
        }

        if (entryPrefab == null || listContainer == null)
        {
            Debug.LogError(
                "UpgradesScreenView: entryPrefab or listContainer is not assigned.",
                this
            );
            return;
        }

        if (Manager == null)
        {
            Debug.LogError(
                "UpgradesScreenView: Manager is not set; this screen must be shown via UiScreenManager.",
                this
            );
            return;
        }

        // UpgradeService should be exposed by UiScreenManager via a public property `UpgradeService`.
        upgradeService = null;
        var prop = Manager.GetType().GetProperty("UpgradeService");
        if (prop != null)
            upgradeService = prop.GetValue(Manager) as UpgradeService;

        if (upgradeService == null)
        {
            Debug.LogError(
                "UpgradesScreenView: UpgradeService could not be resolved from UiScreenManager. "
                    + "Expose a public property `public UpgradeService UpgradeService { get; }` on UiScreenManager and assign it during bootstrap.",
                this
            );
            return;
        }

        if (upgradeService.Wallet == null)
        {
            Debug.LogError(
                "UpgradesScreenView: UpgradeService.Wallet is null (did bootstrap initialize it?).",
                this
            );
            return;
        }

        // Show all upgrades in the catalog. Targeting/effects are derived from resolved modifiers.
        foreach (var upgrade in upgradeCatalog.Upgrades)
        {
            if (upgrade == null)
                continue;

            var entry = Instantiate(entryPrefab, listContainer);
            entry.name = $"Upgrade_{upgrade.id}";

            var purchasedCount = upgradeService
                .PurchasedCount(upgrade.id)
                .DistinctUntilChanged()
                .ToReadOnlyReactiveProperty()
                .AddTo(disposables);

            var summary = BuildUpgradeTargetSummary(gameDefService, upgrade);
            entry.Bind(upgrade, summary, upgradeService, purchasedCount);
        }
    }

    private static string BuildUpgradeTargetSummary(
        GameDefinitionService gameDefService,
        UpgradeEntry upgrade
    )
    {
        if (upgrade == null)
            return "modifier-driven";

        if (gameDefService == null)
            return "modifier-driven";

        var modifiers = gameDefService.ResolveUpgradeModifiers(upgrade.id);
        if (modifiers == null || modifiers.Count == 0)
            return "modifier-driven";

        for (int i = 0; i < modifiers.Count; i++)
        {
            var modifier = modifiers[i];
            if (modifier == null)
                continue;

            var target = (modifier.target ?? string.Empty).Trim();
            var scopeKind = (modifier.scope?.kind ?? string.Empty).Trim();
            var scopeNodeId = (modifier.scope?.nodeId ?? string.Empty).Trim();
            var scopeNodeTag = (modifier.scope?.nodeTag ?? string.Empty).Trim();
            var scopeResource = (modifier.scope?.resource ?? string.Empty).Trim();

            string where = "Global";
            if (
                !string.IsNullOrEmpty(scopeNodeId)
                && gameDefService.TryGetNode(scopeNodeId, out var node)
                && node != null
            )
                where = string.IsNullOrWhiteSpace(node.displayName)
                    ? scopeNodeId
                    : node.displayName;
            else if (!string.IsNullOrEmpty(scopeNodeId))
                where = scopeNodeId;
            else if (!string.IsNullOrEmpty(scopeNodeTag))
                where = $"Tag:{scopeNodeTag}";
            else if (
                string.Equals(scopeKind, "resource", System.StringComparison.OrdinalIgnoreCase)
            )
                where = $"Resource:{scopeResource}";

            string effect = "modifier";
            if (
                target.StartsWith("nodeSpeedMultiplier", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    target,
                    "node.speedMultiplier",
                    System.StringComparison.OrdinalIgnoreCase
                )
            )
                effect = $"speed x{Format.Abbreviated(modifier.value)}";
            else if (
                target.StartsWith("nodeOutput", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    target,
                    "node.outputMultiplier",
                    System.StringComparison.OrdinalIgnoreCase
                )
                || target.StartsWith(
                    "node.outputMultiplier.",
                    System.StringComparison.OrdinalIgnoreCase
                )
            )
                effect = $"output x{Format.Abbreviated(modifier.value)}";
            else if (
                string.Equals(
                    target,
                    "automation.policy",
                    System.StringComparison.OrdinalIgnoreCase
                )
            )
                effect = "automation enabled";
            else if (target.StartsWith("resourceGain", System.StringComparison.OrdinalIgnoreCase))
                effect = $"resource gain x{Format.Abbreviated(modifier.value)}";

            return $"{where} {effect}";
        }

        return "modifier-driven";
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
