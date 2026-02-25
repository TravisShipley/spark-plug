using System;
using TMPro;
using UniRx;
using UnityEngine;

public sealed class BuyModeButtonView : MonoBehaviour
{
    [SerializeField]
    private ReactiveButtonView buttonView;

    [SerializeField]
    private TextMeshProUGUI labelText;

    private readonly CompositeDisposable disposables = new();

    public void Bind(BuyModeViewModel viewModel)
    {
        if (viewModel == null)
            throw new ArgumentNullException(nameof(viewModel));

        disposables.Clear();

        if (buttonView != null)
        {
            var labelStreamForButton = labelText == null ? viewModel.Label : null;
            buttonView.Bind(
                labelText: labelStreamForButton,
                onClick: viewModel.CycleBuyModeCommand.Execute
            );
        }

        if (labelText != null)
        {
            viewModel
                .Label.DistinctUntilChanged()
                .Subscribe(text => labelText.text = text)
                .AddTo(disposables);
        }
    }

    private void OnDestroy()
    {
        disposables.Dispose();
    }
}
