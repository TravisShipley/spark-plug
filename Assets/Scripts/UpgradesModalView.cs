using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
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
        if (Manager is not IGeneratorResolver generatorResolver)
        {
            Debug.LogError(
                "UpgradesModalView: ModalManager must implement IGeneratorResolver.",
                this
            );
            return;
        }

        LogNodeTaggedUpgradeSummaries(upgradeCatalog, generatorResolver);

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

            // For v1, require a GeneratorId so we can wire it. (Global upgrades can be added later.)
            if (string.IsNullOrEmpty(genId))
            {
                Debug.LogWarning(
                    $"UpgradesModalView: Missing generatorId while building entries. {BuildUpgradeDebugContext(upgrade)}",
                    this
                );
                continue;
            }

            if (!generatorResolver.TryGetGenerator(genId, out var generator) || generator == null)
            {
                Debug.LogWarning(
                    $"UpgradesModalView: No generator found for generatorId '{genId}'. {BuildUpgradeDebugContext(upgrade)}",
                    this
                );
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

    private static void LogNodeTaggedUpgradeSummaries(
        UpgradeCatalog upgradeCatalog,
        IGeneratorResolver generatorResolver
    )
    {
        if (upgradeCatalog?.Upgrades == null)
            return;

        var nodeSummaries = upgradeCatalog
            .Upgrades.Where(u => u != null && u.tags != null)
            .Where(u => u.tags.Any(t => string.Equals(t, "node", StringComparison.OrdinalIgnoreCase)))
            .Select(u =>
            {
                var displayName = string.IsNullOrWhiteSpace(u.displayName) ? u.id : u.displayName;

                var generatorId = (u.generatorId ?? string.Empty).Trim();
                var targetName = generatorId;
                if (!string.IsNullOrEmpty(generatorId))
                {
                    if (generatorResolver != null && generatorResolver.TryGetGenerator(generatorId, out var g))
                        targetName = g?.DisplayName ?? generatorId;
                    else
                        Debug.LogWarning(
                            $"UpgradesModalView: Node-tagged upgrade references unresolved generatorId '{generatorId}'. {BuildUpgradeDebugContext(u)}"
                        );
                }
                else
                {
                    Debug.LogWarning(
                        $"UpgradesModalView: Node-tagged upgrade missing generatorId. {BuildUpgradeDebugContext(u)}"
                    );
                    targetName = "Global";
                }

                var effectLabel = u.effectType switch
                {
                    UpgradeEffectType.NodeSpeedMultiplier or UpgradeEffectType.SpeedMultiplier => "Speed",
                    UpgradeEffectType.NodeOutput or UpgradeEffectType.OutputMultiplier => "Output",
                    UpgradeEffectType.ResourceGain => "Resource Gain",
                    UpgradeEffectType.NodeInput => "Input",
                    UpgradeEffectType.NodeCapacityThroughputPerSecond => "Capacity",
                    UpgradeEffectType.StateValue => "State Value",
                    UpgradeEffectType.VariableValue => "Variable Value",
                    UpgradeEffectType.AutomationPolicy => "Automation",
                    _ => u.effectType.ToString(),
                };

                var effectAmount = u.value > 0 ? $" x{Format.Abbreviated(u.value)}" : string.Empty;
                return $"{displayName}. {targetName} {effectLabel}{effectAmount}";
            })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        if (nodeSummaries.Length == 0)
        {
            Debug.Log("UpgradesModalView: No upgrades tagged 'node' found.");
            return;
        }

        foreach (var summary in nodeSummaries)
            Debug.Log($"UpgradesModalView: {summary}");
    }

    private static string BuildUpgradeDebugContext(UpgradeEntry upgrade)
    {
        if (upgrade == null)
            return "upgrade=<null>";

        var name = string.IsNullOrWhiteSpace(upgrade.displayName) ? "<none>" : upgrade.displayName;
        var tags =
            upgrade.tags == null || upgrade.tags.Length == 0 ? "<none>" : string.Join(",", upgrade.tags);
        var effectIds =
            upgrade.effects == null || upgrade.effects.Length == 0
                ? "<none>"
                : string.Join(
                    ",",
                    upgrade.effects
                        .Where(e => e != null && !string.IsNullOrWhiteSpace(e.modifierId))
                        .Select(e => e.modifierId)
                );

        return $"upgradeId='{upgrade.id}', displayName='{name}', category='{upgrade.category}', zoneId='{upgrade.zoneId}', tags=[{tags}], effectType='{upgrade.effectType}', value='{upgrade.value}', modifierIds=[{effectIds}]";
    }
}
