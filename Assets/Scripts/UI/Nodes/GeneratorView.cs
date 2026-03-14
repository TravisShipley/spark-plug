using TMPro;
using UniRx;
using UnityEngine;
using UnityEngine.UI;

public class GeneratorView : MonoBehaviour
{
    #region properties
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
    private GameObject unownedContainer;

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
    private GeneratorProgressBarView progressBarView;

    [SerializeField]
    private Image levelProgressFill;

    private CompositeDisposable disposables = new();
    private GeneratorViewModel viewModel;
    private bool canLevelUpCached;
    #endregion

    public void Bind(GeneratorViewModel vm)
    {
        if (!ValidateRefs())
            return;

        // Allow safe re-binding (e.g., scene reload / reuse) by clearing previous subscriptions.
        disposables.Dispose();
        disposables = new CompositeDisposable();

        Initialize(vm);
        canLevelUpCached = vm.CanLevelUp.Value;

        vm.CanLevelUp.DistinctUntilChanged()
            .Subscribe(value => canLevelUpCached = value)
            .AddTo(disposables);

        progressBarView.Bind(vm);
        BindButtons(vm);
        BindOptionalUi(vm);
    }

    public void Initialize(GeneratorViewModel vm)
    {
        viewModel = vm;

        nameText.text = vm.DisplayName;

        // Output: react to BOTH output and duration changes
        vm.OutputPerCycle.DistinctUntilChanged()
            .Subscribe(output => outputText.text = $"{Format.Currency(output)}")
            .AddTo(disposables);
    }

    #region Private Helper Methods

    private bool ValidateRefs()
    {
        if (nameText == null)
            return Fail(nameof(nameText));
        if (outputText == null)
            return Fail(nameof(outputText));
        if (progressBarView == null)
            return Fail(nameof(progressBarView));
        if (buildButtonView == null)
            return Fail(nameof(buildButtonView));
        if (levelUpButtonView == null)
            return Fail(nameof(levelUpButtonView));
        if (collectButtonView == null)
            return Fail(nameof(collectButtonView));
        if (ownedContainer == null)
            return Fail(nameof(ownedContainer));
        if (unownedContainer == null)
            return Fail(nameof(unownedContainer));

        return true;
    }

    private bool Fail(string fieldName)
    {
        Debug.LogError($"GeneratorView: Missing required ref '{fieldName}'.", this);
        return false;
    }

    private void BindButtons(GeneratorViewModel vm)
    {
        buildButtonView.Bind(
            labelText: vm.NextLevelCost.Select(cost => $"Build\n{Format.Currency(cost)}")
                .DistinctUntilChanged(),
            interactable: vm.BuildCommand.CanExecute,
            visible: vm.BuildCommand.IsVisible,
            onClick: vm.BuildCommand.Execute
        );

        levelUpButtonView.Bind(
            labelText: Observable
                .CombineLatest(
                    vm.BuyModeDisplayName.DistinctUntilChanged(),
                    vm.LevelUpDisplayCost.DistinctUntilChanged(),
                    (modeLabel, cost) => $"Level Up {modeLabel}\n{Format.Currency(cost)}"
                )
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

        if (levelHoldBinder != null)
        {
            levelHoldBinder.Bind(
                canRepeat: () => canLevelUpCached && vm.CanContinueHoldLevelUp(),
                onRepeat: () =>
                {
                    var purchased = vm.TryLevelUpByModeCapped(int.MaxValue);
                    if (purchased <= 0)
                        return;
                },
                onPressStarted: vm.BeginHoldLevelUp,
                onPressEnded: vm.EndHoldLevelUp
            );
        }

        collectButtonView.Bind(
            labelText: Observable.Return("Collect"),
            interactable: vm.CollectCommand.CanExecute,
            visible: vm.CollectCommand.IsVisible,
            onClick: vm.CollectCommand.Execute
        );
    }

    private void BindOptionalUi(GeneratorViewModel vm)
    {
        if (outputText != null)
        {
            outputText.color = outputColor;
            vm.IsOutputBoosted.DistinctUntilChanged()
                .Subscribe(isBoosted =>
                    outputText.color = isBoosted ? boostedOutputColor : outputColor
                )
                .AddTo(disposables);
        }

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
                ownedContainer.SetActive(owned);
                unownedContainer.SetActive(!owned);
                progressBarView.SetVisible(owned);
            })
            .AddTo(disposables);
    }

    #endregion

    private void OnDestroy()
    {
        disposables.Dispose();
    }
}
