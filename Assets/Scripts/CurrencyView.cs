using System;
using UnityEngine;
using TMPro;
using UniRx;

public class CurrencyView : MonoBehaviour
{
    [SerializeField] private CurrencyType currency;
    [SerializeField] private TextMeshProUGUI value;
    private PlayerWalletViewModel walletViewModel;

    public void Initialize(PlayerWalletViewModel viewModel)
    {
        walletViewModel = viewModel;

        if (walletViewModel == null)
        {
            Debug.LogError("CurrencyView: viewModel is null.");
            return;
        }

        if (value == null)
        {
            Debug.LogError("CurrencyView: TextMeshProUGUI reference is null.");
            return;
        }

        var source = currency == CurrencyType.Cash
            ? (IObservable<double>)walletViewModel.CashBalance
            : walletViewModel.GoldBalance;

        // Subscribe and update label (ReactiveProperty emits current value on subscribe)
        source.Subscribe(count => value.text = $"{Format.Currency(count)}")
            .AddTo(this);
    }
}