using System;
using TMPro;
using UniRx;
using UnityEngine;

public sealed class UpgradeEntryView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private TextMeshProUGUI infoText;
    [SerializeField] private ReactiveButtonView buyButton;

    private readonly CompositeDisposable disposables = new CompositeDisposable();

    public void Bind(
        UpgradeDefinition upgrade,
        GeneratorService generator,
        WalletService wallet,
        IReadOnlyReactiveProperty<bool> isPurchased,
        Action markPurchased)
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

        if (wallet == null)
        {
            Debug.LogError("UpgradeEntryView: wallet is null.", this);
            return;
        }

        if (buyButton == null)
        {
            Debug.LogError("UpgradeEntryView: buyButton is not assigned.", this);
            return;
        }

        if (nameText != null)
            nameText.text = upgrade.DisplayName;

        if (infoText != null)
        {
            // Example: "Pizza profits x2" or "Pizza speed x1.1"
            string effectLabel = upgrade.EffectType switch
            {
                UpgradeEffectType.OutputMultiplier => "profits",
                UpgradeEffectType.SpeedMultiplier  => "speed",
                _ => throw new ArgumentOutOfRangeException(
                        nameof(upgrade.EffectType),
                        upgrade.EffectType,
                        "UpgradeEntryView: Unsupported UpgradeEffectType. Did you forget to handle a new enum value?"
                    )
            };

            // Use our truncating formatter for the multiplier value.
            string multText = Format.Abbreviated(upgrade.Value);

            infoText.text = $"{generator.DisplayName} {effectLabel} x{multText}";
        }

        double cost = upgrade.Cost;

        if (costText != null)
            costText.text = Format.Currency(cost);

        var canAfford =
            wallet.CashBalance
                .DistinctUntilChanged()
                .Select(cash => cash >= cost)
                .DistinctUntilChanged();

        var visible =
            isPurchased
                .DistinctUntilChanged()
                .Select(purchased => !purchased)
                .DistinctUntilChanged();

        var interactable =
            Observable.CombineLatest(
                    canAfford,
                    isPurchased.DistinctUntilChanged(),
                    (afford, purchased) => afford && !purchased
                )
                .DistinctUntilChanged();

        buyButton.Bind(
            labelText: Observable.Return($"Buy\n{Format.Currency(cost)}"),
            interactable: interactable,
            visible: visible,
            onClick: () =>
            {
                if (isPurchased.Value) return;
                if (wallet.CashBalance.Value < cost) return;

                wallet.IncrementBalance(CurrencyType.Cash, -cost);
                generator.ApplyUpgrade(upgrade);

                markPurchased?.Invoke();

                // Optional: Hide immediately even if the reactive state update is delayed.
                gameObject.SetActive(false);
            }
        );
    }

    private void OnDestroy()
    {
        disposables.Dispose();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (nameText == null) nameText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (infoText == null)
            infoText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (buyButton == null) buyButton = GetComponentInChildren<ReactiveButtonView>(true);
    }
#endif
}