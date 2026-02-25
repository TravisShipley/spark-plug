using System;
using UniRx;

public class GeneratorViewModel : IDisposable
{
    private readonly GeneratorDefinition definition;
    private readonly GeneratorService generatorService;

    private readonly CompositeDisposable disposables = new();

    public string DisplayName => definition.DisplayName;
    public string LevelCostResourceId => definition.LevelCostResourceId;
    public IReadOnlyReactiveProperty<int> Level => generatorService.Level;
    public IReadOnlyReactiveProperty<bool> IsOwned => generatorService.IsOwned;
    public IReadOnlyReactiveProperty<bool> IsRunning => generatorService.IsRunning;
    public IReadOnlyReactiveProperty<bool> IsAutomated => generatorService.IsAutomated;
    public IReadOnlyReactiveProperty<int> MilestoneRank => generatorService.MilestoneRank;

    // Snapshot accessor kept for non-reactive one-off reads.
    public int MilestoneRankValue => generatorService.MilestoneRank.Value;
    public IReadOnlyReactiveProperty<int> PreviousMilestoneAtLevel =>
        generatorService.PreviousMilestoneAtLevel;
    public IReadOnlyReactiveProperty<int> NextMilestoneAtLevel =>
        generatorService.NextMilestoneAtLevel;
    public IReadOnlyReactiveProperty<float> MilestoneProgressRatio =>
        generatorService.MilestoneProgressRatio;
    public IReadOnlyReactiveProperty<double> CycleDurationSeconds =>
        generatorService.CycleDurationSeconds;
    public IReadOnlyReactiveProperty<double> CycleProgress => generatorService.CycleProgress;

    public IReadOnlyReactiveProperty<double> OutputPerCycle { get; }
    public IReadOnlyReactiveProperty<double> NextLevelCost { get; }
    public IReadOnlyReactiveProperty<bool> CanBuild { get; }
    public IReadOnlyReactiveProperty<bool> CanLevelUp { get; }
    public IReadOnlyReactiveProperty<bool> CanCollect { get; }
    public IReadOnlyReactiveProperty<bool> ShowBuild { get; }
    public IReadOnlyReactiveProperty<bool> ShowLevelUp { get; }
    public IReadOnlyReactiveProperty<bool> ShowCollect { get; }
    public IReadOnlyReactiveProperty<bool> IsOutputBoosted { get; }
    public UiCommand BuildCommand { get; }
    public UiCommand LevelUpCommand { get; }
    public UiCommand CollectCommand { get; }
    public IObservable<Unit> CycleCompleted => generatorService.CycleCompleted;

    public GeneratorViewModel(
        GeneratorModel model,
        GeneratorDefinition definition,
        GeneratorService generatorService,
        WalletViewModel walletViewModel,
        BuffService buffService
    )
    {
        _ = model;
        _ = walletViewModel ?? throw new ArgumentNullException(nameof(walletViewModel));

        this.definition = definition;
        this.generatorService = generatorService;

        OutputPerCycle = Observable
            .CombineLatest(
                generatorService.Level.DistinctUntilChanged(),
                generatorService.OutputMultiplier.DistinctUntilChanged(),
                generatorService.ResourceGainMultiplier.DistinctUntilChanged(),
                (level, outputMult, gainMult) => definition.BaseOutputPerCycle * level * outputMult * gainMult
            )
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        // Source-of-truth cost stream is owned by GeneratorService.
        NextLevelCost = generatorService.NextLevelCostReactive;

        ShowBuild = IsOwned
            .Select(owned => !owned)
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        ShowLevelUp = IsOwned
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        ShowCollect = Observable
            .CombineLatest(
                IsOwned.DistinctUntilChanged(),
                IsAutomated.DistinctUntilChanged(),
                (owned, automated) => owned && !automated
            )
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        IsOutputBoosted =
            buffService != null
                ? buffService.IsActive
                : Observable.Return(false).ToReadOnlyReactiveProperty().AddTo(disposables);

        // Build and level-up share the same service-authored purchasing rule.
        CanBuild = generatorService.CanBuyLevelReactive;
        CanLevelUp = generatorService.CanBuyLevelReactive;
        CanCollect = generatorService.CanCollectReactive;

        BuildCommand = new UiCommand(
            () =>
            {
                if (generatorService.TryBuyLevel())
                    generatorService.StartRun();
            },
            CanBuild,
            ShowBuild
        );

        LevelUpCommand = new UiCommand(
            () => generatorService.TryBuyLevel(),
            CanLevelUp,
            ShowLevelUp
        );

        CollectCommand = new UiCommand(
            () =>
            {
                generatorService.Collect();
                generatorService.StartRun();
            },
            CanCollect,
            ShowCollect
        );
    }

    public void Dispose()
    {
        disposables.Dispose();
    }

    public int TryLevelUpMany(int maxToBuy)
    {
        return generatorService.TryBuyLevels(maxToBuy);
    }
}
