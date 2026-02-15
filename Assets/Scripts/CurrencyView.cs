using System;
using TMPro;
using UniRx;
using UnityEngine;

public class CurrencyView : MonoBehaviour
{
    [SerializeField]
    private string resourceId = "currencySoft";

    [SerializeField]
    private TextMeshProUGUI value;
    private WalletViewModel walletViewModel;

    public void Initialize(WalletViewModel viewModel)
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

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            Debug.LogError("CurrencyView: resourceId is empty.", this);
            return;
        }

        var source = (IObservable<double>)walletViewModel.Balance(resourceId);

        // Subscribe and update label (ReactiveProperty emits current value on subscribe)
        source.Subscribe(count => value.text = $"{Format.Currency(count)}").AddTo(this);
    }
}
