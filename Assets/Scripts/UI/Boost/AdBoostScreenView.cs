using TMPro;
using UniRx;
using UnityEngine;

public sealed class AdBoostScreenView : UiScreenView
{
    [Header("UI")]
    [SerializeField]
    private TMP_Text titleText;

    [SerializeField]
    private TMP_Text countdownText;

    [SerializeField]
    private ReactiveButtonView closeButton;

    [SerializeField]
    private ReactiveButtonView boostButton;

    private readonly CompositeDisposable disposables = new();
    private AdBoostScreenViewModel viewModel;

    public override void OnBeforeShow(object payload)
    {
        disposables.Clear();

        var adBoostViewModel = payload as AdBoostScreenViewModel;
        if (adBoostViewModel == null)
        {
            Debug.LogError("AdBoostScreenView: Expected AdBoostScreenViewModel payload.", this);
            return;
        }

        if (titleText == null || countdownText == null || boostButton == null || closeButton == null)
        {
            Debug.LogError("AdBoostScreenView: Required UI references are missing.", this);
            return;
        }

        viewModel = adBoostViewModel;

        titleText.text = viewModel.Title;
        viewModel.CountdownText.Subscribe(value => countdownText.text = value).AddTo(disposables);

        closeButton.Bind(
            interactable: viewModel.Close.CanExecute,
            visible: viewModel.Close.IsVisible,
            onClick: viewModel.Close.Execute
        );

        boostButton.Bind(
            interactable: viewModel.CanActivate,
            visible: viewModel.ActivateBoost.IsVisible,
            onClick: viewModel.ActivateBoost.Execute
        );
    }

    public override void OnBeforeClose()
    {
        disposables.Clear();
        viewModel = null;
    }
}
