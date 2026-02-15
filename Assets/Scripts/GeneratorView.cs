using TMPro;
using UniRx;
using UnityEngine;
using UnityEngine.UI;

public class GeneratorView : MonoBehaviour
{
    private const float MinCycleDurationSeconds = 0.0001f;
    private const float ProgressCompleteEpsilon = 0.0001f;

    [Header("UI")]
    [SerializeField]
    private TextMeshProUGUI nameText;

    [SerializeField]
    private TextMeshProUGUI levelText;

    [SerializeField]
    private TextMeshProUGUI outputText;

    [SerializeField]
    private GameObject ownedContainer;

    [SerializeField]
    private GameObject progressBar;

    [Header("Buttons")]
    [SerializeField]
    private ReactiveButtonView buildButtonView;

    [SerializeField]
    private ReactiveButtonView levelUpButtonView;

    [SerializeField]
    private ReactiveButtonView collectButtonView;

    [SerializeField]
    private Image progressFill;

    private CompositeDisposable disposables = new();
    private GeneratorViewModel viewModel;
    private GeneratorService generatorService;

    private float cycleStartTime;
    private float lastCycleDuration;

    private enum ProgressState
    {
        Idle, // Not animating (unowned or waiting to start)
        Animating, // Progress bar animates toward full
        Complete, // Bar is full and held at 1.0 until player collects
    }

    private ProgressState progressState;

    public void Bind(
        GeneratorViewModel vm,
        GeneratorService generatorService,
        WalletViewModel walletViewModel
    )
    {
        // Allow safe re-binding (e.g., scene reload / reuse) by clearing previous subscriptions.
        disposables.Dispose();
        disposables = new CompositeDisposable();

        this.generatorService = generatorService;
        Initialize(vm);

        lastCycleDuration = (float)generatorService.CycleDurationSeconds.Value;
        progressState = ProgressState.Idle;

        if (progressFill == null)
        {
            Debug.LogError("GeneratorView: progressFill is not assigned.", this);
            return;
        }

        var nextLevelCost = vm
            .Level.Select(_ => generatorService.NextLevelCost)
            .StartWith(generatorService.NextLevelCost)
            .DistinctUntilChanged();

        var canAffordLevel = Observable
            .CombineLatest(
                walletViewModel.Balance(vm.LevelCostResourceId).DistinctUntilChanged(),
                nextLevelCost,
                (balance, cost) => balance >= cost
            )
            .DistinctUntilChanged();

        var isOwned = vm.IsOwned.DistinctUntilChanged();

        // Start the progress bar running
        generatorService
            .IsRunning.DistinctUntilChanged()
            .Where(running => running)
            .Subscribe(_ =>
            {
                progressState = ProgressState.Animating;

                cycleStartTime = Time.time;
                lastCycleDuration = (float)generatorService.CycleDurationSeconds.Value;

                progressFill.fillAmount = 0f;
            })
            .AddTo(disposables);

        // Manual mode: when the sim stops, keep animating until the bar reaches full, then hold.
        generatorService
            .IsRunning.DistinctUntilChanged()
            .Where(running => !running)
            .Subscribe(_ =>
            {
                if (viewModel != null && viewModel.IsOwned.Value && !viewModel.IsAutomated.Value)
                {
                    // Collect can become available now; we keep animating until the bar reaches full.
                    progressState = ProgressState.Animating;
                }
            })
            .AddTo(disposables);

        // Handle mid-cycle speed changes by preserving elapsed percentage
        generatorService
            .CycleDurationSeconds.DistinctUntilChanged()
            .Subscribe(newDuration =>
            {
                // If we're not actively running a cycle, just update the cached duration.
                if (!generatorService.IsRunning.Value)
                {
                    lastCycleDuration = (float)newDuration;
                    return;
                }

                // Preserve elapsed percentage based on simulation progress to avoid drift.
                float percent = Mathf.Clamp01((float)generatorService.CycleProgress.Value);

                lastCycleDuration = Mathf.Max(MinCycleDurationSeconds, (float)newDuration);
                cycleStartTime = Time.time - percent * lastCycleDuration;
            })
            .AddTo(disposables);

        if (buildButtonView != null)
        {
            var label = nextLevelCost.Select(cost => $"Build\n{Format.Currency(cost)}");

            buildButtonView.Bind(
                labelText: label,
                interactable: canAffordLevel,
                visible: isOwned.Select(x => !x),
                onClick: () =>
                {
                    if (generatorService.TryBuyLevel())
                    {
                        generatorService.StartRun();
                    }
                }
            );
        }

        if (levelUpButtonView != null)
        {
            var label = nextLevelCost.Select(cost => $"Level Up\n{Format.Currency(cost)}");

            levelUpButtonView.Bind(
                labelText: label,
                interactable: canAffordLevel,
                visible: isOwned,
                onClick: () => generatorService.TryBuyLevel()
            );
        }

        if (collectButtonView != null)
        {
            var label = Observable.Return("Collect");

            // Interactable: only when not currently running (i.e., ready for player to start the next cycle)
            var canCollect = generatorService
                .IsRunning.Select(running => !running)
                .DistinctUntilChanged();

            // Visible: only show once owned, and only when NOT automated
            var visible = Observable
                .CombineLatest(
                    vm.IsOwned.DistinctUntilChanged(),
                    vm.IsAutomated.DistinctUntilChanged(),
                    (owned, automated) => owned && !automated
                )
                .DistinctUntilChanged();

            collectButtonView.Bind(
                labelText: label,
                interactable: canCollect,
                visible: visible,
                onClick: () =>
                {
                    // Player-initiated collection: collect now, then immediately start the next cycle.
                    generatorService.Collect();
                    generatorService.StartRun();
                }
            );
        }

        // Visual sync on cycle completion:
        // - Manual mode: animate to full and wait for Collect
        // - Automated mode: immediately restart from 0
        generatorService
            .CycleCompleted.Subscribe(_ =>
            {
                if (viewModel.IsAutomated.Value)
                {
                    progressState = ProgressState.Animating;
                    cycleStartTime = Time.time;
                    lastCycleDuration = (float)generatorService.CycleDurationSeconds.Value;

                    progressFill.fillAmount = 0f;
                }
                else
                {
                    // Manual: collect happened; next StartRun will restart animation.
                    progressState = ProgressState.Idle;
                }
            })
            .AddTo(disposables);
    }

    public void Initialize(GeneratorViewModel vm)
    {
        viewModel = vm;

        nameText.text = vm.DisplayName;

        vm.Level.Subscribe(level => levelText.text = $"Lv {level}").AddTo(disposables);

        // Output: react to BOTH output and duration changes
        vm.OutputPerCycle.DistinctUntilChanged()
            .Subscribe(output => outputText.text = $"{Format.Currency(output)}")
            .AddTo(disposables);

        vm.IsOwned.DistinctUntilChanged()
            .Subscribe(owned =>
            {
                if (ownedContainer != null)
                    ownedContainer.SetActive(owned);
                if (progressBar != null)
                    progressBar.SetActive(owned);
            })
            .AddTo(disposables);
    }

    private void Update()
    {
        if (viewModel == null || !viewModel.IsOwned.Value)
            return;

        if (progressState == ProgressState.Complete)
            return;

        bool isRunning = generatorService.IsRunning.Value;
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
