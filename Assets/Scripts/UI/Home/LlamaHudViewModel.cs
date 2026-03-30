using System;
using System.Globalization;
using UniRx;

public sealed class LlamaHudViewModel : IDisposable
{
    private readonly CompositeDisposable disposables = new();

    public IReadOnlyReactiveProperty<string> LlamaCountText { get; }

    public LlamaHudViewModel(IStateVarService stateVarService)
    {
        if (stateVarService == null)
            throw new ArgumentNullException(nameof(stateVarService));

        LlamaCountText = stateVarService
            .ObserveQuantity("zone.main", "llamas")
            .Select(value => $"Llamas: {Math.Floor(value).ToString("N0", CultureInfo.CurrentCulture)}")
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
