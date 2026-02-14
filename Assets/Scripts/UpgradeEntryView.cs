using TMPro;
using UniRx;
using UnityEngine;

public sealed class UpgradeEntryView : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI nameText;

    [SerializeField]
    private TextMeshProUGUI costText;

    [SerializeField]
    private TextMeshProUGUI infoText;

    [SerializeField]
    private ReactiveButtonView buyButton;

    [SerializeField]
    private GameObject checkmark;

    private readonly CompositeDisposable disposables = new CompositeDisposable();

    public void Bind(
        UpgradeEntry upgrade,
        GeneratorService generator,
        UpgradeService upgradeService,
        IReadOnlyReactiveProperty<int> purchasedCount
    )
    {
        disposables.Clear();

        if (upgrade == null)
        {
            Debug.LogError("UpgradeEntryView: upgrade is null.", this);
            return;
        }

        if (generator == null)
        {
            Debug.LogError("UpgradeEntryView: generator is null.", this);
            return;
        }

        if (upgradeService == null)
        {
            Debug.LogError("UpgradeEntryView: upgradeService is null.", this);
            return;
        }

        if (buyButton == null)
        {
            Debug.LogError("UpgradeEntryView: buyButton is not assigned.", this);
            return;
        }

        var isPurchased = purchasedCount.Select(c => c > 0).DistinctUntilChanged();

        if (checkmark != null)
            checkmark.SetActive(purchasedCount.Value > 0);

        if (nameText != null)
            nameText.text = string.IsNullOrEmpty(upgrade.displayName)
                ? upgrade.id
                : upgrade.displayName;

        if (infoText != null)
        {
            string effectText = upgrade.effectType switch
            {
                UpgradeEffectType.OutputMultiplier => $"profits x{Format.Abbreviated(upgrade.value)}",
                UpgradeEffectType.SpeedMultiplier => $"speed x{Format.Abbreviated(upgrade.value)}",
                UpgradeEffectType.AutomationPolicy => "automation enabled",
                _ => upgrade.effectType.ToString(),
            };

            infoText.text = $"{generator.DisplayName} {effectText}";
        }

        double cost = upgrade.costSimple;
        if (cost <= 0 && upgrade.cost != null && upgrade.cost.Length > 0)
        {
            // Try to parse first cost item's amount as a fallback.
            if (!double.TryParse(upgrade.cost[0].amount, out cost))
                cost = 0;
        }

        if (costText != null)
            costText.text = Format.Currency(cost);

        var canAfford = upgradeService
            .Wallet.CashBalance.DistinctUntilChanged()
            .Select(cash => cash >= cost)
            .DistinctUntilChanged();

        var interactable = Observable
            .CombineLatest(canAfford, isPurchased, (afford, purchased) => afford && !purchased)
            .DistinctUntilChanged();

        isPurchased
            .Subscribe(purchased =>
            {
                if (checkmark != null)
                    checkmark.SetActive(purchased);

                if (buyButton != null)
                    buyButton.gameObject.SetActive(!purchased);
            })
            .AddTo(disposables);

        buyButton.Bind(
            labelText: Observable.Return($"Buy\n{Format.Currency(cost)}"),
            interactable: interactable,
            visible: Observable.Return(true),
            onClick: () => upgradeService.TryPurchase(upgrade.id)
        );
    }

    private void OnDestroy()
    {
        disposables.Dispose();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (nameText == null)
            nameText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (infoText == null)
            infoText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (buyButton == null)
            buyButton = GetComponentInChildren<ReactiveButtonView>(true);
    }
#endif
}
