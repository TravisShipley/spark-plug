using UniRx;
using UnityEngine;
using UnityEngine.UI;

public sealed class GeneratorProgressBarView : MonoBehaviour
{
    private const float MinCycleDurationSeconds = 0.0001f;
    private const float ProgressCompleteEpsilon = 0.0001f;

    [SerializeField]
    private Image progressFill;

    private float cycleStartTime;
    private float lastCycleDuration;
    private ProgressState progressState;
    private readonly CompositeDisposable disposables = new();
    private GeneratorViewModel viewModel;

    private enum ProgressState
    {
        Idle,
        Animating,
        Complete,
    }

    public void Bind(GeneratorViewModel vm)
    {
        if (vm == null)
            throw new System.ArgumentNullException(nameof(vm));

        if (progressFill == null)
        {
            Debug.LogError("GeneratorProgressBarView: Missing required ref 'progressFill'.", this);
            return;
        }

        disposables.Clear();
        viewModel = vm;
        lastCycleDuration = (float)vm.CycleDurationSeconds.Value;
        progressState = ProgressState.Idle;

        BindProgress(vm);
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    private void BindProgress(GeneratorViewModel vm)
    {
        vm.IsRunning.DistinctUntilChanged()
            .Where(running => running)
            .Subscribe(_ =>
            {
                progressState = ProgressState.Animating;
                cycleStartTime = Time.time;
                lastCycleDuration = (float)vm.CycleDurationSeconds.Value;
                progressFill.fillAmount = 0f;
            })
            .AddTo(disposables);

        vm.IsRunning.DistinctUntilChanged()
            .Where(running => !running)
            .Subscribe(_ =>
            {
                if (viewModel != null && viewModel.IsOwned.Value && !viewModel.IsAutomated.Value)
                    progressState = ProgressState.Animating;
            })
            .AddTo(disposables);

        vm.CycleDurationSeconds.DistinctUntilChanged()
            .Subscribe(newDuration =>
            {
                if (!vm.IsRunning.Value)
                {
                    lastCycleDuration = (float)newDuration;
                    return;
                }

                float percent = Mathf.Clamp01((float)vm.CycleProgress.Value);
                lastCycleDuration = Mathf.Max(MinCycleDurationSeconds, (float)newDuration);
                cycleStartTime = Time.time - percent * lastCycleDuration;
            })
            .AddTo(disposables);

        vm.CycleCompleted.Subscribe(_ =>
            {
                if (viewModel.IsAutomated.Value)
                {
                    progressState = ProgressState.Animating;
                    cycleStartTime = Time.time;
                    lastCycleDuration = (float)vm.CycleDurationSeconds.Value;
                    progressFill.fillAmount = 0f;
                }
                else
                {
                    progressState = ProgressState.Idle;
                }
            })
            .AddTo(disposables);
    }

    private void Update()
    {
        if (viewModel == null)
            return;

        if (!viewModel.IsOwned.Value)
            return;

        if (progressState == ProgressState.Complete)
            return;

        bool isRunning = viewModel.IsRunning.Value;
        if (!isRunning && progressState != ProgressState.Animating)
            return;

        float duration = Mathf.Max(MinCycleDurationSeconds, lastCycleDuration);
        float t = (Time.time - cycleStartTime) / duration;
        float fill = Mathf.Clamp01(t);
        progressFill.fillAmount = fill;

        if (progressState == ProgressState.Animating && fill >= 1f - ProgressCompleteEpsilon)
        {
            progressState = ProgressState.Complete;
            progressFill.fillAmount = 1f;
        }
    }

    private void OnDestroy()
    {
        disposables.Dispose();
    }
}
