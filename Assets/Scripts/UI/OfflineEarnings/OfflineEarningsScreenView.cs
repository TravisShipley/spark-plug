using TMPro;
using UniRx;
using UnityEngine;

public sealed class OfflineEarningsScreenView : UiScreenView
{
    [Header("UI")]
    [SerializeField]
    private TextMeshProUGUI titleText;

    [SerializeField]
    private TextMeshProUGUI summaryText;

    [SerializeField]
    private TextMeshProUGUI earningsText;

    [SerializeField]
    private ReactiveButtonView collectButton;

    [SerializeField]
    private ReactiveButtonView closeButton;

    private readonly CompositeDisposable disposables = new();
    private OfflineEarningsViewModel viewModel;

    public override void OnBeforeShow(object payload)
    {
        disposables.Clear();

        var offlineViewModel = payload as OfflineEarningsViewModel;
        if (offlineViewModel == null)
        {
            Debug.LogError(
                "OfflineEarningsScreenView: Expected OfflineEarningsViewModel payload.",
                this
            );
            return;
        }

        viewModel = offlineViewModel;

        if (
            titleText == null
            || summaryText == null
            || earningsText == null
            || collectButton == null
        )
        {
            Debug.LogError("OfflineEarningsScreenView: Required UI references are missing.", this);
            return;
        }

        viewModel.Title.Subscribe(value => titleText.text = value).AddTo(disposables);
        viewModel.Summary.Subscribe(value => summaryText.text = value).AddTo(disposables);
        viewModel.EarningsLine.Subscribe(value => earningsText.text = value).AddTo(disposables);

        collectButton.Bind(
            interactable: viewModel.Collect.CanExecute,
            visible: viewModel.Collect.IsVisible,
            onClick: viewModel.Collect.Execute
        );

        if (closeButton != null)
        {
            closeButton.Bind(
                interactable: Observable.Return(true),
                visible: Observable.Return(true),
                onClick: RequestClose
            );
        }
    }

    public override void OnBeforeClose()
    {
        disposables.Clear();
        viewModel?.Dispose();
        viewModel = null;
    }
}
