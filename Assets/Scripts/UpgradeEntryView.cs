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

    public void Bind(UpgradeEntryViewModel viewModel)
    {
        disposables.Clear();

        if (viewModel == null)
        {
            Debug.LogError("UpgradeEntryView: viewModel is null.", this);
            return;
        }

        if (buyButton == null)
        {
            Debug.LogError("UpgradeEntryView: buyButton is not assigned.", this);
            return;
        }

        if (checkmark != null)
            checkmark.SetActive(viewModel.IsMaxed.Value);

        if (nameText != null)
            nameText.text = viewModel.Title;

        if (infoText != null)
            infoText.text = viewModel.Summary;

        if (costText != null)
            costText.text = viewModel.CostText;

        viewModel
            .IsMaxed
            .Subscribe(maxed =>
            {
                if (checkmark != null)
                    checkmark.SetActive(maxed);
            })
            .AddTo(disposables);

        buyButton.Bind(
            labelText: Observable.Return(viewModel.CostLabel),
            interactable: viewModel.Purchase.CanExecute,
            visible: viewModel.Purchase.IsVisible,
            onClick: viewModel.Purchase.Execute
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
