using System;
using UniRx;

public sealed class UpgradeEntryViewModel : IDisposable
{
    private readonly CompositeDisposable disposables = new();

    public string UpgradeId { get; }
    public string DisplayName { get; }
    public string InfoText { get; }
    public string CostText { get; }
    public string CostLabel { get; }
    public bool Repeatable { get; }
    public bool IsValidDefinition { get; }

    public IReadOnlyReactiveProperty<int> PurchasedCount { get; }
    public IReadOnlyReactiveProperty<bool> IsOwned { get; }
    public IReadOnlyReactiveProperty<bool> IsMaxed { get; }
    public IReadOnlyReactiveProperty<bool> IsAffordable { get; }
    public IReadOnlyReactiveProperty<bool> CanPurchase { get; }
    public IReadOnlyReactiveProperty<bool> IsVisible { get; }
    public UiCommand PurchaseCommand { get; }

    // Backward-compatible aliases for existing bindings.
    public string Title => DisplayName;
    public string Summary => InfoText;
    public IReadOnlyReactiveProperty<bool> CanAfford => IsAffordable;
    public UiCommand Purchase => PurchaseCommand;

    public UpgradeEntryViewModel(
        UpgradeDefinition upgrade,
        UpgradeService upgradeService,
        string summary,
        string costResourceId,
        double costAmount,
        bool isValidDefinition
    )
    {
        if (upgrade == null)
            throw new ArgumentNullException(nameof(upgrade));
        if (upgradeService == null)
            throw new ArgumentNullException(nameof(upgradeService));

        UpgradeId = (upgrade.id ?? string.Empty).Trim();
        DisplayName = string.IsNullOrWhiteSpace(upgrade.displayName)
            ? UpgradeId
            : upgrade.displayName.Trim();
        InfoText = string.IsNullOrWhiteSpace(summary) ? "modifier-driven" : summary.Trim();
        CostText = Format.Currency(costAmount);
        CostLabel = $"Buy\n{CostText}";
        Repeatable = upgrade.repeatable;
        IsValidDefinition = isValidDefinition;

        PurchasedCount = upgradeService.PurchasedCount(UpgradeId);
        IsOwned = PurchasedCount
            .Select(count => count > 0)
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        IsMaxed = PurchasedCount
            .Select(_ => upgradeService.IsAtMaxRank(UpgradeId))
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        // Re-evaluate service-authored affordability/can-purchase whenever
        // either wallet balance for this upgrade's primary cost resource or
        // purchased count changes.
        var affordabilityTrigger = Observable
            .CombineLatest(
                upgradeService.Wallet.GetBalanceProperty(costResourceId).DistinctUntilChanged(),
                PurchasedCount.DistinctUntilChanged(),
                (balance, _) => balance
            )
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        IsAffordable = affordabilityTrigger
            .Select(_ => upgradeService.CanAfford(UpgradeId))
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        CanPurchase = affordabilityTrigger
            .Select(_ => isValidDefinition && upgradeService.CanPurchase(UpgradeId))
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        IsVisible = IsMaxed
            .Select(maxed => upgrade.enabled && !maxed)
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        PurchaseCommand = new UiCommand(
            () => upgradeService.TryPurchase(UpgradeId),
            CanPurchase,
            IsVisible
        );
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
