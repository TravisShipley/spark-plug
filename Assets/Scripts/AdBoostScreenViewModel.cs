using System;
using UniRx;

public sealed class AdBoostScreenViewModel : IDisposable
{
    public const string CanonicalBuffId = "buff.adBoost.profit2x";

    private readonly CompositeDisposable disposables = new();
    private readonly BuffService buffService;
    private readonly string buffId;

    public string Title => "Ad Boost";
    public IReadOnlyReactiveProperty<bool> IsActive { get; }
    public IReadOnlyReactiveProperty<bool> CanActivate { get; }
    public IReadOnlyReactiveProperty<string> CountdownText { get; }
    public UiCommand ActivateBoost { get; }
    public UiCommand Close { get; }

    public AdBoostScreenViewModel(BuffService buffService, BuffCatalog buffCatalog, Action close)
    {
        this.buffService = buffService ?? throw new ArgumentNullException(nameof(buffService));
        if (buffCatalog == null)
            throw new ArgumentNullException(nameof(buffCatalog));
        if (close == null)
            throw new ArgumentNullException(nameof(close));

        if (!buffCatalog.TryGet(CanonicalBuffId, out _))
        {
            throw new InvalidOperationException(
                $"AdBoostScreenViewModel: required buff '{CanonicalBuffId}' was not found in game_definition.json buffs[]."
            );
        }

        buffId = CanonicalBuffId;
        IsActive = this.buffService.IsActive;

        CanActivate = IsActive
            .Select(active => !active)
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        CountdownText = Observable
            .CombineLatest(
                IsActive,
                this.buffService.RemainingSeconds,
                (active, remainingSeconds) =>
                    active ? $"{TimeFormat.FormatCountdown(remainingSeconds)}" : "Ready"
            )
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        ActivateBoost = new UiCommand(() => this.buffService.Activate(buffId), CanActivate);
        Close = new UiCommand(close);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
