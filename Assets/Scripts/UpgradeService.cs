using System;
using System.Collections.Generic;
using UniRx;

public interface IGeneratorResolver
{
    bool TryGetGenerator(string generatorId, out GeneratorService generator);
}

public sealed class UpgradeService : IDisposable
{
    private readonly UpgradeCatalog catalog;
    private readonly WalletService wallet;
    private readonly SaveService saveService;
    private readonly IGeneratorResolver generatorResolver;

    // UpgradeId -> PurchasedCount (reactive)
    private readonly Dictionary<string, ReactiveProperty<int>> purchasedCountById = new Dictionary<
        string,
        ReactiveProperty<int>
    >(StringComparer.Ordinal);

    public UpgradeService(
        UpgradeCatalog catalog,
        WalletService wallet,
        SaveService saveService,
        IGeneratorResolver generatorResolver
    )
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
        this.generatorResolver =
            generatorResolver ?? throw new ArgumentNullException(nameof(generatorResolver));
    }

    public WalletService Wallet => wallet;

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
        double cost = GetCost(def);
        return wallet.CashBalance.Value >= cost;
    }

    public bool CanPurchase(string upgradeId)
    {
        var def = catalog.GetRequired(upgradeId);
        if (!def.enabled)
            return false;

        var count = PurchasedCount(def.id).Value;
        if (IsAtMaxRank(def, count))
            return false;

        var generatorId = (def.generatorId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(generatorId))
            return false;

        if (!generatorResolver.TryGetGenerator(generatorId, out var generator) || generator == null)
            return false;

        double cost = GetCost(def);
        return wallet.CashBalance.Value >= cost;
    }

    public bool TryPurchase(string upgradeId)
    {
        var def = catalog.GetRequired(upgradeId);
        if (!def.enabled)
            return false;

        var count = PurchasedCount(def.id).Value;
        if (IsAtMaxRank(def, count))
            return false;

        // v1: only supports generator-targeted upgrades (global handled later)
        var generatorId = (def.generatorId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(generatorId))
            return false;

        if (!generatorResolver.TryGetGenerator(generatorId, out var generator) || generator == null)
            return false;

        double cost = GetCost(def);
        if (wallet.CashBalance.Value < cost)
            return false;

        // Spend
        wallet.IncrementBalance(CurrencyType.Cash, -cost);

        // Apply effect once (repeatable purchases are simply multiple applications)
        generator.ApplyUpgrade(def);

        // Update state
        var rp = (ReactiveProperty<int>)PurchasedCount(def.id);
        rp.Value = rp.Value + 1;

        // Persist upgrade purchase facts immediately.
        SaveInto(saveService.Data);
        saveService.RequestSave();

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
    }

    /// <summary>
    /// Apply saved upgrades to generators (call after generators are registered).
    /// </summary>
    public void ApplyAllPurchased()
    {
        // NOTE: This method assumes generators are in a clean baseline state.
        // It must only be called once after load to avoid double-applying upgrades.
        foreach (var kv in purchasedCountById)
        {
            var id = kv.Key;
            var count = kv.Value?.Value ?? 0;
            if (count <= 0)
                continue;

            UpgradeEntry def;
            try
            {
                def = catalog.GetRequired(id);
            }
            catch
            {
                continue;
            }

            var generatorId = (def.generatorId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(generatorId))
                continue;

            if (
                !generatorResolver.TryGetGenerator(generatorId, out var generator)
                || generator == null
            )
                continue;

            for (int i = 0; i < count; i++)
                generator.ApplyUpgrade(def);
        }
    }

    private static double GetCost(UpgradeEntry def)
    {
        if (def == null)
            return 0;
        // Prefer explicit simple cost
        if (def.costSimple > 0)
            return def.costSimple;

        // Fallback: try parse first cost item
        if (def.cost != null && def.cost.Length > 0)
        {
            if (double.TryParse(def.cost[0].amount, out var v))
                return v;
        }

        return 0;
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
        foreach (var kv in purchasedCountById)
            kv.Value?.Dispose();

        purchasedCountById.Clear();
    }
}
