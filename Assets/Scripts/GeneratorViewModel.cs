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
    public IReadOnlyReactiveProperty<bool> IsAutomated => generatorService.IsAutomated;
    public IReadOnlyReactiveProperty<int> MilestoneRank => generatorService.MilestoneRank;
    public int MilestoneRankValue => generatorService.MilestoneRank.Value;
    public IReadOnlyReactiveProperty<int> PreviousMilestoneAtLevel =>
        generatorService.PreviousMilestoneAtLevel;
    public IReadOnlyReactiveProperty<int> NextMilestoneAtLevel =>
        generatorService.NextMilestoneAtLevel;
    public IReadOnlyReactiveProperty<float> MilestoneProgressRatio =>
        generatorService.MilestoneProgressRatio;

    public IReadOnlyReactiveProperty<double> OutputPerCycle { get; }
    public IObservable<Unit> CycleCompleted => generatorService.CycleCompleted;

    public GeneratorViewModel(
        GeneratorModel model,
        GeneratorDefinition definition,
        GeneratorService generatorService
    )
    {
        this.definition = definition;
        this.generatorService = generatorService;

        OutputPerCycle = Observable
            .CombineLatest(
                generatorService.Level.DistinctUntilChanged(),
                generatorService.OutputMultiplier.DistinctUntilChanged(),
                (level, mult) => definition.BaseOutputPerCycle * level * mult
            )
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }

    public void Collect(double cashGenerated)
    {
        generatorService.HandleCollectPressed(cashGenerated);
    }
}
