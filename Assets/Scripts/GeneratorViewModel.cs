using UniRx;
using System;

public class GeneratorViewModel : IDisposable
{
    private readonly GeneratorDefinition definition;
    private readonly GeneratorService generatorService;
    
    private readonly CompositeDisposable disposables = new();

    public string DisplayName => definition.DisplayName;
    public IReadOnlyReactiveProperty<int> Level => generatorService.Level;
    public IReadOnlyReactiveProperty<bool> IsOwned => generatorService.IsOwned;
    public IReadOnlyReactiveProperty<bool> IsAutomated => generatorService.IsAutomated;

    public IReadOnlyReactiveProperty<double> OutputPerCycle { get; }
    public IObservable<Unit> CycleCompleted => generatorService.CycleCompleted;

    public GeneratorViewModel(GeneratorModel model, GeneratorDefinition definition, GeneratorService generatorService)
    {
        this.definition = definition;
        this.generatorService = generatorService;

        OutputPerCycle =
            Observable
                .CombineLatest(
                    generatorService.Level.DistinctUntilChanged(),
                    generatorService.OutputMultiplier.DistinctUntilChanged(),
                    (level, mult) => definition.BaseOutputPerCycle * level * mult
                )
                .ToReadOnlyReactiveProperty()
                .AddTo(disposables);
    }

    // public void SetAutomated(bool value)
    // {
    //     generatorService.SetAutomated(value);
    // }

    public void Dispose()
    {
        disposables.Dispose();
    }
}