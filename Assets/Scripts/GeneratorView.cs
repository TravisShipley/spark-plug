using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UniRx;

public class GeneratorView : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI outputText;
    [SerializeField] private GameObject ownedContainer;
    [SerializeField] private GameObject progressBar;

    [Header("Buttons")]
    [SerializeField] private ReactiveButtonView buildButtonView;
    [SerializeField] private ReactiveButtonView levelUpButtonView;
    [SerializeField] private ReactiveButtonView collectButtonView;
    [SerializeField] private ReactiveButtonView automateButtonView;
    [SerializeField] private Image progressFill;

    private CompositeDisposable disposables = new();
    private GeneratorViewModel viewModel;
    private GeneratorService generatorService;

    private float cycleStartTime;
    private float lastCycleDuration;
    private bool waitingForCollect;

    public void Bind(GeneratorViewModel vm, GeneratorService generatorService, PlayerWalletViewModel walletViewModel)
    {
        // Allow safe re-binding (e.g., scene reload / reuse) by clearing previous subscriptions.
        disposables.Dispose();
        disposables = new CompositeDisposable();

        this.generatorService = generatorService;
        Initialize(vm);

        lastCycleDuration = (float)generatorService.CycleDurationSeconds.Value;
        waitingForCollect = false;

        var nextLevelCost =
                vm.Level
                  .Select(_ => generatorService.NextLevelCost)
                  .StartWith(generatorService.NextLevelCost)
                  .DistinctUntilChanged();

        var canAffordLevel =
            Observable.CombineLatest(
                    walletViewModel.CashBalance.DistinctUntilChanged(),
                    nextLevelCost,
                    (cash, cost) => cash >= cost
                )
                .DistinctUntilChanged();

        var isOwned = vm.IsOwned.DistinctUntilChanged();

        // Start the progress bar running
        generatorService.IsRunning
            .DistinctUntilChanged()
            .Where(running => running)
            .Subscribe(_ =>
            {
                waitingForCollect = false;

                cycleStartTime = Time.time;
                lastCycleDuration = (float)generatorService.CycleDurationSeconds.Value;

                if (progressFill != null)
                    progressFill.fillAmount = 0f;
            })
            .AddTo(disposables);

        // Handle mid-cycle speed changes by preserving elapsed percentage
        generatorService.CycleDurationSeconds
            .DistinctUntilChanged()
            .Subscribe(newDuration =>
            {
                // If we're not actively running a cycle, just update the cached duration.
                if (generatorService == null || !generatorService.IsRunning.Value)
                {
                    lastCycleDuration = (float)newDuration;
                    return;
                }

                // Preserve elapsed percentage based on simulation progress to avoid drift.
                float percent = 0f;
                if (generatorService.CycleProgress != null)
                    percent = Mathf.Clamp01((float)generatorService.CycleProgress.Value);

                lastCycleDuration = Mathf.Max(0.0001f, (float)newDuration);
                cycleStartTime = Time.time - percent * lastCycleDuration;
            })
            .AddTo(disposables);

        if (buildButtonView != null)
        {
            var label =
                nextLevelCost.Select(cost => $"Build\n${cost:0.00}");

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
            var label =
                nextLevelCost.Select(cost => $"Level Up\n${cost:0.00}");

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
            var canCollect =
                generatorService.IsRunning
                    .Select(running => !running)
                    .DistinctUntilChanged();

            // Visible: only show once owned, and only when NOT automated
            var visible =
                Observable.CombineLatest(
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

        if (automateButtonView != null)
        {
            // Automation cost is owned by the service/definition (per-generator)
            double automationCost = generatorService.AutomationCost;

            // Label (constant for now; if you ever make automation cost dynamic, turn this into a reactive stream)
            var label = Observable.Return($"Automate\n${automationCost:0.00}");

            // Interactable: can afford automation
            var canAfford =
                walletViewModel.CashBalance
                    .DistinctUntilChanged()
                    .Select(cash => cash >= automationCost)
                    .DistinctUntilChanged();

            // Visible: only show if owned and not already automated
            var visible =
                Observable.CombineLatest(
                        vm.IsOwned.DistinctUntilChanged(),
                        vm.IsAutomated.DistinctUntilChanged(),
                        (owned, automated) => owned && !automated
                    )
                    .DistinctUntilChanged();

            automateButtonView.Bind(
                labelText: label,
                interactable: canAfford,
                visible: visible,
                onClick: () => generatorService.TryBuyAutomation()
            );
        }

        // Visual sync on cycle completion:
        // - Manual mode: snap to full and wait for Collect
        // - Automated mode: immediately restart from 0
        generatorService.CycleCompleted
            .Subscribe(_ =>
            {
                if (viewModel != null && viewModel.IsAutomated.Value)
                {
                    waitingForCollect = false;
                    cycleStartTime = Time.time;
                    lastCycleDuration = (float)generatorService.CycleDurationSeconds.Value;

                    if (progressFill != null)
                        progressFill.fillAmount = 0f;
                }
                else
                {
                    waitingForCollect = true;
                    if (progressFill != null)
                        progressFill.fillAmount = 1f;
                }
            })
            .AddTo(disposables);
    }

    public void Initialize(GeneratorViewModel vm)
    {
        viewModel = vm;

        nameText.text = vm.DisplayName;

        vm.Level
            .Subscribe(level => levelText.text = $"Lv {level}")
            .AddTo(disposables);

        // Output: react to BOTH output and duration changes
        var durationStream =
            this.generatorService != null
                ? this.generatorService.CycleDurationSeconds.DistinctUntilChanged()
                : Observable.Return(0.0);

        Observable
            .CombineLatest(
                vm.OutputPerCycle.DistinctUntilChanged(),
                durationStream,
                (output, duration) => $"{Format.Currency(output)} / {duration:0.00}s"
            )
            .Subscribe(text => outputText.text = text)
            .AddTo(disposables);

        vm.IsOwned
            .DistinctUntilChanged()
            .Subscribe(owned =>
            {
                if (ownedContainer != null)
                    ownedContainer.SetActive(owned);
            })
            .AddTo(disposables);
    }

    private void Update()
    {
        if (viewModel == null || !viewModel.IsOwned.Value || progressFill == null)
            return;

        // Manual mode: once completed, hold at full until Collect.
        if (waitingForCollect)
            return;

        // Only animate while the generator is running.
        if (generatorService == null || !generatorService.IsRunning.Value)
            return;

        float duration = Mathf.Max(0.0001f, lastCycleDuration);
        float t = (Time.time - cycleStartTime) / duration;
        progressFill.fillAmount = Mathf.Clamp01(t);
    }

    private void OnDestroy()
    {
        disposables.Dispose();
    }
}