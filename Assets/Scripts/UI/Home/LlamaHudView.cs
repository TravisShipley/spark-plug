using TMPro;
using UniRx;
using UnityEngine;

public sealed class LlamaHudView : MonoBehaviour
{
    [SerializeField]
    private TMP_Text llamaText;

    private readonly CompositeDisposable disposables = new();
    private LlamaHudViewModel viewModel;

    public void Initialize(LlamaHudViewModel viewModel)
    {
        if (viewModel == null)
        {
            Debug.LogError("LlamaHudView: viewModel is null.", this);
            return;
        }

        if (llamaText == null)
        {
            Debug.LogError("LlamaHudView: llamaText is not assigned.", this);
            return;
        }

        this.viewModel = viewModel;
        Rebind();
    }

    private void OnEnable()
    {
        Rebind();
    }

    private void OnDisable()
    {
        disposables.Clear();
    }

    private void OnDestroy()
    {
        disposables.Dispose();
    }

    private void Rebind()
    {
        disposables.Clear();
        if (!isActiveAndEnabled || viewModel == null || llamaText == null)
            return;

        viewModel.LlamaCountText.Subscribe(text => llamaText.text = text).AddTo(disposables);
    }
}
