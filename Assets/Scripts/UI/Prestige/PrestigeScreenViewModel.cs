using System;
using UniRx;

public sealed class PrestigeScreenViewModel : IDisposable
{
    private readonly CompositeDisposable disposables = new();
    private readonly PrestigeService prestigeService;

    public string Title => "Prestige";
    public IReadOnlyReactiveProperty<string> PreviewGain { get; }
    public IReadOnlyReactiveProperty<string> CurrentMeta { get; }
    public IReadOnlyReactiveProperty<bool> CanPrestige => prestigeService.CanPrestige;
    public UiCommand PerformPrestige { get; }
    public UiCommand Close { get; }

    public PrestigeScreenViewModel(PrestigeService prestigeService, Action close)
    {
        this.prestigeService =
            prestigeService ?? throw new ArgumentNullException(nameof(prestigeService));
        if (close == null)
            throw new ArgumentNullException(nameof(close));

        PreviewGain = this.prestigeService
            .PreviewGain.Select(gain => Format.Abbreviated(gain))
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        CurrentMeta = this.prestigeService
            .CurrentMetaBalance.Select(balance => Format.Abbreviated(balance))
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        PerformPrestige = new UiCommand(this.prestigeService.PerformPrestige, this.prestigeService.CanPrestige);
        Close = new UiCommand(close);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
