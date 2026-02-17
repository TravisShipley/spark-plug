using System;
using UniRx;

public sealed class UpgradeEntryViewModel : IDisposable
{
    private readonly CompositeDisposable disposables = new();

    public string UpgradeId { get; }
    public string Title { get; }
    public string Summary { get; }
    public string CostText { get; }
    public string CostLabel { get; }
    public bool Repeatable { get; }
    public bool IsValidDefinition { get; }

    public IReadOnlyReactiveProperty<int> PurchasedCount { get; }
    public IReadOnlyReactiveProperty<bool> IsOwned { get; }
    public IReadOnlyReactiveProperty<bool> IsMaxed { get; }
    public IReadOnlyReactiveProperty<bool> CanAfford { get; }
    public UiCommand Purchase { get; }

    public UpgradeEntryViewModel(
        UpgradeEntry upgrade,
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
        Title = string.IsNullOrWhiteSpace(upgrade.displayName) ? UpgradeId : upgrade.displayName.Trim();
        Summary = string.IsNullOrWhiteSpace(summary) ? "modifier-driven" : summary.Trim();
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

        int maxPurchases = upgrade.repeatable ? (upgrade.maxRank > 0 ? upgrade.maxRank : int.MaxValue) : 1;

        IsMaxed = PurchasedCount
            .Select(count => count >= maxPurchases)
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        CanAfford = upgradeService
            .Wallet.GetBalanceProperty(costResourceId)
            .DistinctUntilChanged()
            .Select(balance => balance >= costAmount)
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        var canPurchase = Observable
            .CombineLatest(
                CanAfford,
                IsMaxed,
                (afford, maxed) => upgrade.enabled && isValidDefinition && afford && !maxed
            )
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        var isVisible = IsMaxed
            .Select(maxed => upgrade.enabled && !maxed)
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        Purchase = new UiCommand(
            () => upgradeService.TryPurchase(UpgradeId),
            canPurchase,
            isVisible
        );
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
