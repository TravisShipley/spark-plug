using UniRx;
using UnityEngine;
using UnityEngine.UI;

public class GeneratorButtonView : MonoBehaviour
{
    [SerializeField]
    private Button runButton;

    [SerializeField]
    private double cashGenerated;

    private GeneratorViewModel viewModel;

    public void Bind(GeneratorViewModel viewModel)
    {
        this.viewModel = viewModel;
    }

    private void Start()
    {
        runButton
            .onClick.AsObservable()
            .Subscribe(_ =>
            {
                if (viewModel == null)
                {
                    Debug.LogError(
                        "GeneratorButtonView: viewModel is not bound before click.",
                        this
                    );
                    return;
                }

                viewModel.Collect(cashGenerated);
            })
            .AddTo(this);
    }
}
