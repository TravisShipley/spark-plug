using TMPro;
using UniRx;
using UnityEngine;

public sealed class PrestigeScreenView : UiScreenView
{
    [Header("UI")]
    [SerializeField]
    private TMP_Text titleText;

    [SerializeField]
    private TMP_Text previewGainText;

    [SerializeField]
    private TMP_Text currentMetaText;

    [SerializeField]
    private ReactiveButtonView closeButton;

    [SerializeField]
    private ReactiveButtonView prestigeButton;

    private readonly CompositeDisposable disposables = new();

    public override void OnBeforeShow(object payload)
    {
        disposables.Clear();

        var viewModel = payload as PrestigeScreenViewModel;
        if (viewModel == null)
        {
            Debug.LogError("PrestigeScreenView: Expected PrestigeScreenViewModel payload.", this);
            return;
        }

        if (
            titleText == null
            || previewGainText == null
            || currentMetaText == null
            || closeButton == null
            || prestigeButton == null
        )
        {
            Debug.LogError("PrestigeScreenView: Required UI references are missing.", this);
            return;
        }

        titleText.text = viewModel.Title;
        viewModel.PreviewGain.Subscribe(value => previewGainText.text = value).AddTo(disposables);
        viewModel.CurrentMeta.Subscribe(value => currentMetaText.text = value).AddTo(disposables);

        closeButton.Bind(
            interactable: viewModel.Close.CanExecute,
            visible: viewModel.Close.IsVisible,
            onClick: viewModel.Close.Execute
        );

        prestigeButton.Bind(
            interactable: viewModel.CanPrestige,
            visible: viewModel.PerformPrestige.IsVisible,
            onClick: viewModel.PerformPrestige.Execute
        );
    }

    public override void OnBeforeClose()
    {
        disposables.Clear();
    }
}
