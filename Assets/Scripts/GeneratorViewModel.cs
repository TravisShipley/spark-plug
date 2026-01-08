using UniRx;
using System;
using UnityEngine;

public class GeneratorViewModel : IDisposable
{
    private readonly GeneratorModel model;
    private readonly GeneratorDefinition definition;
    private readonly GeneratorService generatorService;
    
    private readonly CompositeDisposable disposables = new();

    public string Name => definition.DisplayName;
    public IReadOnlyReactiveProperty<int> Level => generatorService.Level;
    public IReadOnlyReactiveProperty<bool> IsOwned => generatorService.IsOwned;
    public IReadOnlyReactiveProperty<bool> IsAutomated => generatorService.IsAutomated;

    public IReadOnlyReactiveProperty<double> OutputPerCycle { get; }
    public IReadOnlyReactiveProperty<double> CycleDurationSeconds { get; }
    public IObservable<Unit> CycleCompleted => generatorService.CycleCompleted;

    public GeneratorViewModel(GeneratorModel model, GeneratorDefinition definition, GeneratorService generatorService)
    {
        this.model = model;
        this.definition = definition;
        this.generatorService = generatorService;

        OutputPerCycle =
            generatorService.Level
                .Select(l => definition.BaseOutputPerCycle * l)
                .ToReadOnlyReactiveProperty()
                .AddTo(disposables);

        CycleDurationSeconds =
            Observable.Return(definition.BaseCycleDurationSeconds)
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