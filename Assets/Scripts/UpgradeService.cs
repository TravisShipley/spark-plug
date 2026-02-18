using System;
using System.Collections.Generic;
using System.Globalization;
using UniRx;
using UnityEngine;

public sealed class UpgradeService : IDisposable
{
    private readonly UpgradeCatalog catalog;
    private readonly WalletService wallet;
    private readonly SaveService saveService;
    private readonly CompositeDisposable disposables = new();
    private readonly Dictionary<string, ModifierEntry> modifiersById;
    private readonly HashSet<string> invalidUpgradeErrorsLogged = new(StringComparer.Ordinal);
    private readonly HashSet<string> affordabilityScanErrorsLogged = new(StringComparer.Ordinal);
    private readonly Subject<string> purchasedStateChanged = new();
    private readonly ReadOnlyReactiveProperty<bool> hasAffordableUpgrades;
    private readonly ReadOnlyReactiveProperty<bool> hasAffordableManagers;

    // UpgradeId -> PurchasedCount (reactive)
    private readonly Dictionary<string, ReactiveProperty<int>> purchasedCountById = new Dictionary<
        string,
        ReactiveProperty<int>
    >(StringComparer.Ordinal);

    public UpgradeService(
        UpgradeCatalog catalog,
        WalletService wallet,
        SaveService saveService,
        IReadOnlyList<ModifierEntry> modifiers
    )
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
        modifiersById = new Dictionary<string, ModifierEntry>(StringComparer.Ordinal);
        if (modifiers != null)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i];
                var modifierId = (modifier?.id ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(modifierId) || modifiersById.ContainsKey(modifierId))
                    continue;

                modifiersById[modifierId] = modifier;
            }
        }

        var affordabilityRecompute = Observable
            .Merge(ObserveRelevantWalletBalanceChanges(), purchasedStateChanged.Select(_ => Unit.Default))
            .ThrottleFrame(1)
            .StartWith(Unit.Default)
            .Publish()
            .RefCount();

        hasAffordableUpgrades = affordabilityRecompute
            .Select(_ => HasAnyAffordableUpgrade(IsNonAutomationUpgrade))
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        hasAffordableManagers = affordabilityRecompute
            .Select(_ => HasAnyAffordableUpgrade(IsAutomationUpgrade))
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);
    }

    public WalletService Wallet => wallet;
    public IReadOnlyReactiveProperty<bool> HasAffordableUpgrades => hasAffordableUpgrades;
    public IReadOnlyReactiveProperty<bool> HasAffordableManagers => hasAffordableManagers;

    public IObservable<string> PurchasedStateChangedAsObservable() => purchasedStateChanged;

    public IReadOnlyReactiveProperty<int> PurchasedCount(string upgradeId)
    {
        upgradeId = (upgradeId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(upgradeId))
            return new ReactiveProperty<int>(0);

        if (!purchasedCountById.TryGetValue(upgradeId, out var rp) || rp == null)
        {
            rp = new ReactiveProperty<int>(0);
            purchasedCountById[upgradeId] = rp;
        }

        return rp;
    }

    public bool IsPurchased(string upgradeId) => PurchasedCount(upgradeId).Value > 0;

    public int PurchasedCountValue(string upgradeId) => PurchasedCount(upgradeId).Value;

    public bool IsAtMaxRank(string upgradeId)
    {
        var def = catalog.GetRequired(upgradeId);
        return IsAtMaxRank(def, PurchasedCount(def.id).Value);
    }

    public bool CanAfford(string upgradeId)
    {
        var def = catalog.GetRequired(upgradeId);
        return CanAffordCost(GetRequiredCost(def));
    }

    public bool CanPurchase(string upgradeId)
    {
        var def = catalog.GetRequired(upgradeId);
        if (!def.enabled)
            return false;

        if (!HasValidModifierReferences(def.id, out _))
            return false;

        var count = PurchasedCount(def.id).Value;
        if (IsAtMaxRank(def, count))
            return false;

        return CanAffordCost(GetRequiredCost(def));
    }

    public bool TryPurchase(string upgradeId)
    {
        var def = catalog.GetRequired(upgradeId);
        if (!def.enabled)
            return false;

        if (!HasValidModifierReferences(def.id, out var validationError))
        {
            LogInvalidUpgradeDefinition(def.id, validationError);
            return false;
        }

        var count = PurchasedCount(def.id).Value;
        if (IsAtMaxRank(def, count))
            return false;

        if (!wallet.TrySpend(GetRequiredCost(def)))
            return false;

        // Update state
        var rp = (ReactiveProperty<int>)PurchasedCount(def.id);
        rp.Value = rp.Value + 1;

        // Persist upgrade purchase facts immediately.
        SaveInto(saveService.Data);
        saveService.RequestSave();
        purchasedStateChanged.OnNext(def.id);

        return true;
    }

    /// <summary>
    /// Load purchased counts from save, but do NOT spend currency.
    /// </summary>
    public void LoadFrom(GameData data)
    {
        if (data?.Upgrades == null)
            return;

        foreach (var s in data.Upgrades)
        {
            if (s == null)
                continue;

            var id = (s.Id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
                continue;

            int count = Math.Max(0, s.PurchasedCount);
            var rp = (ReactiveProperty<int>)PurchasedCount(id);
            rp.Value = count;
        }

        purchasedStateChanged.OnNext("load");
    }

    /// <summary>
    /// Apply saved upgrades to generators (call after generators are registered).
    /// </summary>
    public void ApplyAllPurchased()
    {
        // Retained as a lifecycle hook for callers that expect this after generators are composed.
        // Modifiers are now authoritative and consume purchased state directly.
        purchasedStateChanged.OnNext("apply_all");
    }

    private bool CanAffordCost(CostItem[] cost)
    {
        if (cost == null || cost.Length == 0)
            return true;

        for (int i = 0; i < cost.Length; i++)
        {
            var item = cost[i];
            if (item == null)
                return false;

            if (
                !double.TryParse(
                    item.amount,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out var amount
                )
            )
                return false;

            if (wallet.GetBalance(item.resource) < amount)
                return false;
        }

        return true;
    }

    private static CostItem[] GetRequiredCost(UpgradeEntry def)
    {
        if (def?.cost == null || def.cost.Length == 0)
        {
            throw new InvalidOperationException(
                $"UpgradeService: Upgrade '{def?.id}' is missing cost entries."
            );
        }

        return def.cost;
    }

    private static int GetMaxPurchases(UpgradeEntry def)
    {
        if (def == null)
            return 0;

        if (!def.repeatable)
            return 1;

        // repeatable + maxRank <= 0 means uncapped repeat purchases.
        if (def.maxRank <= 0)
            return int.MaxValue;

        return def.maxRank;
    }

    private static bool IsAtMaxRank(UpgradeEntry def, int purchasedCount)
    {
        int maxPurchases = GetMaxPurchases(def);
        if (maxPurchases <= 0)
            return true;

        return purchasedCount >= maxPurchases;
    }

    /// <summary>
    /// Write current purchased counts into save data.
    /// </summary>
    public void SaveInto(GameData data)
    {
        if (data == null)
            return;

        data.Upgrades ??= new List<GameData.UpgradeStateData>();
        data.Upgrades.Clear();

        foreach (var kv in purchasedCountById)
        {
            int count = kv.Value?.Value ?? 0;
            if (count <= 0)
                continue;

            data.Upgrades.Add(
                new GameData.UpgradeStateData { Id = kv.Key, PurchasedCount = count }
            );
        }
    }

    public void Dispose()
    {
        disposables.Dispose();

        purchasedStateChanged.OnCompleted();
        purchasedStateChanged.Dispose();

        foreach (var kv in purchasedCountById)
            kv.Value?.Dispose();

        purchasedCountById.Clear();
    }

    public IReadOnlyDictionary<string, int> GetPurchasedCountsSnapshot()
    {
        var snapshot = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in purchasedCountById)
        {
            var count = kv.Value?.Value ?? 0;
            if (count > 0)
                snapshot[kv.Key] = count;
        }

        return snapshot;
    }

    public bool HasValidModifierReferences(string upgradeId, out string error)
    {
        error = null;

        var def = catalog.GetRequired(upgradeId);
        if (def == null)
        {
            error = $"Upgrade '{upgradeId}' is missing from catalog.";
            return false;
        }

        if (def.effects == null || def.effects.Length == 0)
        {
            error = $"Upgrade '{def.id}' has no effects[].modifierId.";
            return false;
        }

        for (int i = 0; i < def.effects.Length; i++)
        {
            var effect = def.effects[i];
            if (effect == null)
            {
                error = $"Upgrade '{def.id}' has null effects[{i}].";
                return false;
            }

            var modifierId = (effect.modifierId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(modifierId))
            {
                error = $"Upgrade '{def.id}' has empty effects[{i}].modifierId.";
                return false;
            }

            if (!modifiersById.ContainsKey(modifierId))
            {
                error =
                    $"Upgrade '{def.id}' references missing modifierId '{modifierId}' in effects[{i}].";
                return false;
            }
        }

        return true;
    }

    private void LogInvalidUpgradeDefinition(string upgradeId, string details)
    {
        var id = (upgradeId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(id))
            id = "unknown";

        if (!invalidUpgradeErrorsLogged.Add(id))
            return;

        Debug.LogError($"UpgradeService: {details}");
    }

    private IObservable<Unit> ObserveRelevantWalletBalanceChanges()
    {
        var streams = new List<IObservable<Unit>>();
        var seenResources = new HashSet<string>(StringComparer.Ordinal);
        var upgrades = catalog.Upgrades;
        if (upgrades == null || upgrades.Count == 0)
            return Observable.Empty<Unit>();

        for (int i = 0; i < upgrades.Count; i++)
        {
            var upgrade = upgrades[i];
            if (upgrade?.cost == null || upgrade.cost.Length == 0)
                continue;

            for (int costIndex = 0; costIndex < upgrade.cost.Length; costIndex++)
            {
                var cost = upgrade.cost[costIndex];
                var resourceId = (cost?.resource ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(resourceId) || !seenResources.Add(resourceId))
                    continue;

                try
                {
                    streams.Add(
                        wallet
                            .GetBalanceProperty(resourceId)
                            .DistinctUntilChanged()
                            .Skip(1)
                            .Select(_ => Unit.Default)
                    );
                }
                catch (Exception ex)
                {
                    LogAffordabilityScanError(
                        upgrade.id,
                        $"Skipping badge scan subscription for unknown resource '{resourceId}': {ex.Message}"
                    );
                }
            }
        }

        return streams.Count > 0 ? Observable.Merge(streams) : Observable.Empty<Unit>();
    }

    private bool HasAnyAffordableUpgrade(Func<UpgradeEntry, bool> includeFilter)
    {
        var upgrades = catalog.Upgrades;
        if (upgrades == null || upgrades.Count == 0)
            return false;

        for (int i = 0; i < upgrades.Count; i++)
        {
            var upgrade = upgrades[i];
            if (upgrade == null || !includeFilter(upgrade))
                continue;

            if (TryCanPurchase(upgrade.id, out var canPurchase) && canPurchase)
                return true;
        }

        return false;
    }

    private bool TryCanPurchase(string upgradeId, out bool canPurchase)
    {
        canPurchase = false;
        var id = (upgradeId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(id))
            return false;

        if (!catalog.TryGet(id, out var upgrade) || upgrade == null)
            return false;

        if (upgrade.cost == null || upgrade.cost.Length == 0)
            return false;

        try
        {
            canPurchase = CanPurchase(id);
            return true;
        }
        catch (Exception ex)
        {
            LogAffordabilityScanError(id, ex.Message);
            return false;
        }
    }

    private static bool IsAutomationUpgrade(UpgradeEntry upgrade)
    {
        var category = (upgrade?.category ?? string.Empty).Trim();
        return string.Equals(category, "Automation", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNonAutomationUpgrade(UpgradeEntry upgrade) =>
        !IsAutomationUpgrade(upgrade);

    private void LogAffordabilityScanError(string upgradeId, string details)
    {
        var id = (upgradeId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(id))
            id = "unknown";

        if (!affordabilityScanErrorsLogged.Add(id))
            return;

        Debug.LogError($"UpgradeService: Badge affordability scan failed for '{id}': {details}");
    }
}
