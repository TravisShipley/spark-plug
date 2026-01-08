using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UniRx;
// using System.Diagnostics;

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

    private float cycleStartTime;

    public void Bind(GeneratorViewModel vm, GeneratorService generatorService, PlayerWalletViewModel walletViewModel)
    {
        // Allow safe re-binding (e.g., scene reload / reuse) by clearing previous subscriptions.
        disposables.Dispose();
        disposables = new CompositeDisposable();

        Initialize(vm);

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
                    cycleStartTime = Time.time;   // start visual cycle now
                    if (progressFill != null)
                        progressFill.fillAmount = 0f;
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
                onClick: () => {
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
                        (isOwned, isAutomated) => isOwned && !isAutomated
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
                        (isOwned, isAutomated) => isOwned && !isAutomated
                    )
                    .DistinctUntilChanged();

            automateButtonView.Bind(
                labelText: label,
                interactable: canAfford,
                visible: visible,
                onClick: () => generatorService.TryBuyAutomation()
            );
        }

        // Visual sync: reset the progress bar instantly when a cycle completes.
        generatorService.CycleCompleted
            .Subscribe(_ =>
            {
                cycleStartTime = Time.time;
                if (progressFill != null)
                    progressFill.fillAmount = 0f;
            })
            .AddTo(disposables);
    }

    public void Initialize(GeneratorViewModel vm)
    {
        viewModel = vm;

        nameText.text = vm.Name;

        vm.Level
            .Subscribe(level =>
                levelText.text = $"Lv {level}"
            )
            .AddTo(disposables);

        // Output: react to BOTH output and duration changes
        Observable
            .CombineLatest(
                vm.OutputPerCycle.DistinctUntilChanged(),
                vm.CycleDurationSeconds.DistinctUntilChanged(),
                (output, duration) => $"${output:0.00} / {duration:0.00}s"
            )
            .Subscribe(text => outputText.text = text)
            .AddTo(disposables);

        vm.IsOwned
            .DistinctUntilChanged()
            .Subscribe(isOwned =>
            {
                if (ownedContainer != null)
                    ownedContainer.SetActive(isOwned);
            })
            .AddTo(disposables);
    }

    private void Update()
    {
        if (viewModel == null || !viewModel.IsOwned.Value || progressFill == null)
            return;

        Debug.Log("update");
        float duration = (float)viewModel.CycleDurationSeconds.Value;
        duration = Mathf.Max(0.0001f, duration);

        float t = (Time.time - cycleStartTime) / duration;
        progressFill.fillAmount = Mathf.Clamp01(t);
    }

    private void OnDestroy()
    {
        disposables.Dispose();
    }
}