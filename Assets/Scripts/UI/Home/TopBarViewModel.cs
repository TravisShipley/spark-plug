using System;
using Ignition.Binding;
using Ignition.Commands;
using UniRx;

public sealed class TopBarViewModel : IDisposable
{
    private readonly CompositeDisposable disposables = new();

    [Bindable]
    public IReadOnlyReactiveProperty<string> WarpLabel { get; }

    [BindableCommand]
    public ICommand Warp { get; }

    public TopBarViewModel(TimeWarpService timeWarpService)
    {
        if (timeWarpService == null)
            throw new ArgumentNullException(nameof(timeWarpService));

        WarpLabel = Observable.Return("Warp").ToReadOnlyReactiveProperty().AddTo(disposables);
        Warp = new UiCommand(() => timeWarpService.ApplyWarp(14400d));
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
