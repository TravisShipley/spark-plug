using System;
using System.Collections.Generic;
using UniRx;

public interface IGeneratorResolver
{
    bool TryGetGenerator(string generatorId, out GeneratorService generator);
}

public sealed class UpgradeService : IDisposable
{
    private readonly UpgradeDatabase upgradeDatabase;
    private readonly WalletService wallet;
    private readonly IGeneratorResolver generatorResolver;

    // UpgradeId -> PurchasedCount (reactive)
    private readonly Dictionary<string, ReactiveProperty<int>> purchasedCountById =
        new Dictionary<string, ReactiveProperty<int>>(StringComparer.Ordinal);

    public UpgradeService(UpgradeDatabase upgradeDatabase, WalletService wallet, IGeneratorResolver generatorResolver)
    {
        this.upgradeDatabase = upgradeDatabase ?? throw new ArgumentNullException(nameof(upgradeDatabase));
        this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        this.generatorResolver = generatorResolver ?? throw new ArgumentNullException(nameof(generatorResolver));
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

    public bool CanAfford(string upgradeId)
    {
        var def = upgradeDatabase.GetRequired(upgradeId);
        return wallet.CashBalance.Value >= def.Cost;
    }

    public bool TryPurchase(string upgradeId)
    {
        var def = upgradeDatabase.GetRequired(upgradeId);

        // v1: only supports generator-targeted upgrades (global handled later)
        var generatorId = (def.GeneratorId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(generatorId))
            return false;

        if (!generatorResolver.TryGetGenerator(generatorId, out var generator) || generator == null)
            return false;

        if (wallet.CashBalance.Value < def.Cost)
            return false;

        // Spend
        wallet.IncrementBalance(CurrencyType.Cash, -def.Cost);

        // Apply effect once (repeatable purchases are simply multiple applications)
        generator.ApplyUpgrade(def);

        // Update state
        var rp = (ReactiveProperty<int>)PurchasedCount(def.Id);
        rp.Value = rp.Value + 1;

        return true;
    }

    /// <summary>
    /// Load purchased counts from save, but do NOT spend currency.
    /// </summary>
    public void LoadFrom(GameData data)
    {
        if (data?.Upgrades == null) return;

        foreach (var s in data.Upgrades)
        {
            if (s == null) continue;

            var id = (s.Id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id)) continue;

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
            if (count <= 0) continue;

            UpgradeDefinition def;
            try { def = upgradeDatabase.GetRequired(id); }
            catch { continue; }

            var generatorId = (def.GeneratorId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(generatorId)) continue;

            if (!generatorResolver.TryGetGenerator(generatorId, out var generator) || generator == null)
                continue;

            for (int i = 0; i < count; i++)
                generator.ApplyUpgrade(def);
        }
    }

    /// <summary>
    /// Write current purchased counts into save data.
    /// </summary>
    public void SaveInto(GameData data)
    {
        if (data == null) return;

        data.Upgrades ??= new List<GameData.UpgradeStateData>();
        data.Upgrades.Clear();

        foreach (var kv in purchasedCountById)
        {
            int count = kv.Value?.Value ?? 0;
            if (count <= 0) continue;

            data.Upgrades.Add(new GameData.UpgradeStateData
            {
                Id = kv.Key,
                PurchasedCount = count
            });
        }
    }

    public void Dispose()
    {
        foreach (var kv in purchasedCountById)
            kv.Value?.Dispose();

        purchasedCountById.Clear();
    }
}