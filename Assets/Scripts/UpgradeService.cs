using System;
using System.Collections.Generic;
using System.Globalization;
using UniRx;

public sealed class UpgradeService : IDisposable
{
    private readonly UpgradeCatalog catalog;
    private readonly WalletService wallet;
    private readonly SaveService saveService;
    private readonly Subject<string> purchasedStateChanged = new();

    // UpgradeId -> PurchasedCount (reactive)
    private readonly Dictionary<string, ReactiveProperty<int>> purchasedCountById = new Dictionary<
        string,
        ReactiveProperty<int>
    >(StringComparer.Ordinal);

    public UpgradeService(
        UpgradeCatalog catalog,
        WalletService wallet,
        SaveService saveService
    )
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
    }

    public WalletService Wallet => wallet;

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
}
