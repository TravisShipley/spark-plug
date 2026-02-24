using System;
using System.Collections.Generic;
using System.Globalization;
using UniRx;
using UnityEngine;

public class WalletService : IDisposable
{
    private readonly CompositeDisposable disposables = new();
    private readonly SaveService saveService;
    private readonly ResourceCatalog resourceCatalog;
    private readonly GameEventStream gameEventStream;
    private ModifierService modifierService;
    private readonly Dictionary<string, ReactiveProperty<double>> balancesByResourceId = new(
        StringComparer.Ordinal
    );

    public WalletService(
        SaveService saveService,
        ResourceCatalog resourceCatalog,
        GameEventStream gameEventStream
    )
    {
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
        this.resourceCatalog =
            resourceCatalog ?? throw new ArgumentNullException(nameof(resourceCatalog));
        this.gameEventStream =
            gameEventStream ?? throw new ArgumentNullException(nameof(gameEventStream));

        InitializeBalances();
        LoadFromSave();
        WirePersistence();

        this.gameEventStream
            .IncrementBalance.Subscribe(tuple => Add(tuple.resourceId, tuple.amount))
            .AddTo(disposables);
    }

    public double GetBalance(string resourceId)
    {
        return GetBalanceProperty(resourceId).Value;
    }

    public IReadOnlyReactiveProperty<double> GetBalanceProperty(string resourceId)
    {
        return GetBalancePropertyInternal(resourceId);
    }

    public void Add(string resourceId, double amount)
    {
        AddInternal(resourceId, amount, applyResourceGainMultiplier: true);
    }

    public void AddRaw(string resourceId, double amount)
    {
        AddInternal(resourceId, amount, applyResourceGainMultiplier: false);
    }

    public void ApplyOfflineEarnings(OfflineSessionResult result)
    {
        if (result == null || result.ResourceGains == null)
            return;

        for (int i = 0; i < result.ResourceGains.Count; i++)
        {
            var gain = result.ResourceGains[i];
            if (gain == null)
                continue;

            // OfflineProgressCalculator already applies global resource-gain multipliers.
            AddInternal(gain.resourceId, gain.amount, applyResourceGainMultiplier: false);
        }
    }

    public void SetModifierService(ModifierService modifierService)
    {
        this.modifierService = modifierService;
    }

    public bool TrySpend(CostItem[] cost)
    {
        if (cost == null || cost.Length == 0)
            return true;

        for (int i = 0; i < cost.Length; i++)
        {
            var item = cost[i];
            if (item == null)
                throw new InvalidOperationException(
                    $"WalletService.TrySpend: Null cost at index {i}."
                );

            var amount = ParseRequiredAmount(item);
            if (amount < 0)
            {
                throw new InvalidOperationException(
                    $"WalletService.TrySpend: Negative cost amount '{amount}' for resource '{item.resource}'."
                );
            }

            var balance = GetBalance(item.resource);
            if (balance < amount)
                return false;
        }

        for (int i = 0; i < cost.Length; i++)
        {
            var item = cost[i];
            var amount = ParseRequiredAmount(item);
            if (amount <= 0)
                continue;

            var balance = GetBalancePropertyInternal(item.resource);
            balance.Value -= amount;
        }
        return true;
    }

    private ReactiveProperty<double> GetBalancePropertyInternal(string resourceId)
    {
        var normalized = NormalizeResourceId(resourceId);
        if (!balancesByResourceId.TryGetValue(normalized, out var balance) || balance == null)
        {
            throw new InvalidOperationException(
                $"WalletService: Unknown resource '{normalized}'. Validate game content and callers."
            );
        }

        return balance;
    }

    private string NormalizeResourceId(string resourceId)
    {
        var normalized = (resourceId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized))
            throw new InvalidOperationException("WalletService: resourceId cannot be empty.");

        return normalized;
    }

    private static double ParseRequiredAmount(CostItem item)
    {
        if (item == null)
            throw new InvalidOperationException("WalletService: cost item cannot be null.");

        if (
            !double.TryParse(
                item.amount,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var amount
            )
        )
        {
            throw new InvalidOperationException(
                $"WalletService: Unable to parse cost amount '{item.amount}' for resource '{item.resource}'."
            );
        }

        return amount;
    }

    private void InitializeBalances()
    {
        var resources = resourceCatalog.Resources;
        if (resources == null || resources.Count == 0)
        {
            throw new InvalidOperationException(
                "WalletService: ResourceCatalog is empty. game_definition.json must define at least one resource."
            );
        }

        for (int i = 0; i < resources.Count; i++)
        {
            var definition = resources[i];
            var id = (definition?.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidOperationException(
                    $"WalletService: Resource definition at index {i} is missing id."
                );
            }

            if (balancesByResourceId.ContainsKey(id))
            {
                throw new InvalidOperationException(
                    $"WalletService: Duplicate resource id '{id}'."
                );
            }

            var balance = new ReactiveProperty<double>(0);
            balancesByResourceId[id] = balance;
        }
    }

    private void WirePersistence()
    {
        foreach (var kv in balancesByResourceId)
        {
            kv.Value?.Skip(1).Subscribe(_ => PersistToSave()).AddTo(disposables);
        }
    }

    private void LoadFromSave()
    {
        var data = saveService.Data;
        if (data == null)
            return;

        data.Resources ??= new List<GameData.ResourceBalanceData>();
        for (int i = 0; i < data.Resources.Count; i++)
        {
            var entry = data.Resources[i];
            if (entry == null)
                continue;

            var id = (entry.ResourceId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
                continue;

            if (!balancesByResourceId.TryGetValue(id, out var balance) || balance == null)
            {
                Debug.LogError(
                    $"WalletService: Save references unknown resource '{id}'. Check game_definition.json."
                );
                continue;
            }

            balance.Value = entry.Amount;
        }
    }

    private void PersistToSave()
    {
        foreach (var kv in balancesByResourceId)
        {
            saveService.SetResourceBalance(kv.Key, kv.Value?.Value ?? 0d, requestSave: false);
        }

        saveService.RequestSave();
    }

    public void Dispose()
    {
        foreach (var kv in balancesByResourceId)
            kv.Value?.Dispose();
        balancesByResourceId.Clear();

        disposables.Dispose();
    }

    private void AddInternal(
        string resourceId,
        double amount,
        bool applyResourceGainMultiplier
    )
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount))
            throw new InvalidOperationException(
                $"WalletService.Add: Invalid amount '{amount}' for resource '{resourceId}'."
            );

        if (Math.Abs(amount) < double.Epsilon)
            return;

        if (amount > 0d && applyResourceGainMultiplier && modifierService != null)
            amount *= modifierService.GetResourceGainMultiplier(resourceId);

        var normalizedResourceId = NormalizeResourceId(resourceId);
        if (amount > 0d && IsSoftCurrency(normalizedResourceId))
            saveService.AddLifetimeEarnings(normalizedResourceId, amount, requestSave: false);

        var balance = GetBalancePropertyInternal(normalizedResourceId);
        balance.Value += amount;
    }

    private bool IsSoftCurrency(string resourceId)
    {
        if (!resourceCatalog.TryGet(resourceId, out var definition) || definition == null)
            return false;

        return string.Equals(
            (definition.kind ?? string.Empty).Trim(),
            "softCurrency",
            StringComparison.OrdinalIgnoreCase
        );
    }
}
