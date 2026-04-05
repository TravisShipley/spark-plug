using System;
using TMPro;
using UniRx;
using UnityEngine;

public sealed class ResourceView : MonoBehaviour
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
            Debug.LogError("ResourceView: viewModel is null.", this);
            return;
        }

        if (value == null)
        {
            Debug.LogError("ResourceView: TextMeshProUGUI reference is null.", this);
            return;
        }

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            Debug.LogError("ResourceView: resourceId is empty.", this);
            return;
        }

        if (!walletViewModel.TryGetResourceDefinition(resourceId, out var definition))
        {
            Debug.LogError(
                $"ResourceView: Unknown resource '{resourceId}'. Check scene wiring and content.",
                this
            );
            return;
        }

        var source = (IObservable<double>)walletViewModel.Balance(resourceId);
        source
            .Subscribe(amount => value.text = ResourceTextFormatter.FormatResource(definition, amount))
            .AddTo(this);
    }
}
