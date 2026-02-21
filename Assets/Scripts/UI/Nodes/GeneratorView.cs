using System;
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
    private TextMeshProUGUI outputText;

    [SerializeField]
    private TextMeshProUGUI levelText;

    [SerializeField]
    private TextMeshProUGUI nextMilestoneText;

    [SerializeField]
    private GameObject ownedContainer;

    [SerializeField]
    private Color outputColor = Color.white;

    [SerializeField]
    private Color boostedOutputColor = Color.white;

    [Header("Buttons")]
    [SerializeField]
    private ReactiveButtonView buildButtonView;

    [SerializeField]
    private ReactiveButtonView levelUpButtonView;

    [SerializeField]
    private HoldRepeatButtonBinder levelHoldBinder;

    [SerializeField]
    private ReactiveButtonView collectButtonView;

    [Header("Progress")]
    [SerializeField]
    private GameObject progressBar;

    [SerializeField]
    private Image progressFill;

    [SerializeField]
    private Image levelProgressFill;

    private CompositeDisposable disposables = new();
    private GeneratorViewModel viewModel;

    private float cycleStartTime;
    private float lastCycleDuration;
    private bool canLevelUpCached;
    private int currentLevelCached;
    private int nextMilestoneLevelCached;
    private int holdBuyBudgetRemaining;
    private int levelsPerPurchase = 1;

    private enum ProgressState
    {
        Idle, // Not animating (unowned or waiting to start)
        Animating, // Progress bar animates toward full
        Complete, // Bar is full and held at 1.0 until player collects
    }

    private ProgressState progressState;

    public void Bind(GeneratorViewModel vm)
    {
        // Allow safe re-binding (e.g., scene reload / reuse) by clearing previous subscriptions.
        disposables.Dispose();
        disposables = new CompositeDisposable();

        Initialize(vm);

        lastCycleDuration = (float)vm.CycleDurationSeconds.Value;
        progressState = ProgressState.Idle;
        canLevelUpCached = vm.CanLevelUp.Value;
        currentLevelCached = vm.Level.Value;
        nextMilestoneLevelCached = vm.NextMilestoneAtLevel.Value;
        holdBuyBudgetRemaining = int.MaxValue;

        vm.CanLevelUp.DistinctUntilChanged()
            .Subscribe(value => canLevelUpCached = value)
            .AddTo(disposables);
        vm.Level.DistinctUntilChanged()
            .Subscribe(value => currentLevelCached = value)
            .AddTo(disposables);
        vm.NextMilestoneAtLevel.DistinctUntilChanged()
            .Subscribe(value => nextMilestoneLevelCached = value)
            .AddTo(disposables);

        if (progressFill == null)
        {
            Debug.LogError("GeneratorView: progressFill is not assigned.", this);
            return;
        }

        // Start the progress bar running
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

        // Manual mode: when the sim stops, keep animating until the bar reaches full, then hold.
        vm.IsRunning.DistinctUntilChanged()
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
        vm.CycleDurationSeconds.DistinctUntilChanged()
            .Subscribe(newDuration =>
            {
                // If we're not actively running a cycle, just update the cached duration.
                if (!vm.IsRunning.Value)
                {
                    lastCycleDuration = (float)newDuration;
                    return;
                }

                // Preserve elapsed percentage based on simulation progress to avoid drift.
                float percent = Mathf.Clamp01((float)vm.CycleProgress.Value);

                lastCycleDuration = Mathf.Max(MinCycleDurationSeconds, (float)newDuration);
                cycleStartTime = Time.time - percent * lastCycleDuration;
            })
            .AddTo(disposables);

        if (buildButtonView != null)
        {
            buildButtonView.Bind(
                labelText: vm.NextLevelCost.Select(cost => $"Build\n{Format.Currency(cost)}")
                    .DistinctUntilChanged(),
                interactable: vm.BuildCommand.CanExecute,
                visible: vm.BuildCommand.IsVisible,
                onClick: vm.BuildCommand.Execute
            );
        }

        if (levelUpButtonView != null)
        {
            levelUpButtonView.Bind(
                labelText: vm.NextLevelCost.Select(cost => $"Level Up\n{Format.Currency(cost)}")
                    .DistinctUntilChanged(),
                interactable: vm.LevelUpCommand.CanExecute,
                visible: vm.LevelUpCommand.IsVisible,
                onClick: () =>
                {
                    if (levelHoldBinder != null && levelHoldBinder.ConsumeSuppressNextClick())
                        return;

                    vm.LevelUpCommand.Execute();
                }
            );
        }

        if (levelHoldBinder != null)
        {
            levelHoldBinder.Bind(
                canRepeat: () => canLevelUpCached && holdBuyBudgetRemaining > 0,
                onRepeat: () =>
                {
                    var purchased = vm.TryLevelUpMany(levelsPerPurchase);
                    if (purchased <= 0)
                        return;

                    if (holdBuyBudgetRemaining != int.MaxValue)
                        holdBuyBudgetRemaining = Math.Max(0, holdBuyBudgetRemaining - purchased);
                },
                onPressStarted: () =>
                {
                    if (nextMilestoneLevelCached > currentLevelCached)
                    {
                        holdBuyBudgetRemaining = nextMilestoneLevelCached - currentLevelCached;
                        return;
                    }

                    holdBuyBudgetRemaining = int.MaxValue;
                }
            );
        }

        if (collectButtonView != null)
        {
            collectButtonView.Bind(
                labelText: Observable.Return("Collect"),
                interactable: vm.CollectCommand.CanExecute,
                visible: vm.CollectCommand.IsVisible,
                onClick: vm.CollectCommand.Execute
            );
        }

        // Visual sync on cycle completion:
        // - Manual mode: animate to full and wait for Collect
        // - Automated mode: immediately restart from 0
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

        // Output: react to BOTH output and duration changes
        vm.OutputPerCycle.DistinctUntilChanged()
            .Subscribe(output => outputText.text = $"{Format.Currency(output)}")
            .AddTo(disposables);

        if (outputText != null)
        {
            outputText.color = outputColor;
            vm.IsOutputBoosted.DistinctUntilChanged()
                .Subscribe(isBoosted =>
                    outputText.color = isBoosted ? boostedOutputColor : outputColor
                )
                .AddTo(disposables);
        }

        // levelText displays the MilestoneRank + 1
        if (levelText != null)
        {
            vm.MilestoneRank.DistinctUntilChanged()
                .Subscribe(rank => levelText.text = $"Lv. {rank + 1}")
                .AddTo(disposables);
        }

        if (nextMilestoneText != null)
        {
            Observable
                .CombineLatest(
                    vm.Level.DistinctUntilChanged(),
                    vm.NextMilestoneAtLevel.DistinctUntilChanged(),
                    (currentLevel, nextLevel) => (currentLevel, nextLevel)
                )
                .Subscribe(pair =>
                {
                    nextMilestoneText.text =
                        pair.nextLevel > 0 ? $"{pair.currentLevel} / {pair.nextLevel}" : "Max";
                })
                .AddTo(disposables);
        }

        if (levelProgressFill != null)
        {
            vm.MilestoneProgressRatio.DistinctUntilChanged()
                .Subscribe(fill => levelProgressFill.fillAmount = Mathf.Clamp01(fill))
                .AddTo(disposables);
        }

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
