using System;
using Ignition.Binding;
using Ignition.Commands;
using UniRx;

public sealed class TopBarViewModel : IDisposable
{
    private const string SoftCurrencyResourceId = "currencySoft";
    private const string HardCurrencyResourceId = "currencyHard";
    private const string woolResourceId = "wool";

    private readonly CompositeDisposable disposables = new();
    private readonly ReactiveProperty<string> softCurrencyText;
    private readonly ReactiveProperty<string> hardCurrencyText;
    private readonly ReactiveProperty<string> woolText;

    [Bindable]
    public IReadOnlyReactiveProperty<string> WarpLabel { get; }

    [Bindable("SoftCurrency")]
    public IReadOnlyReactiveProperty<string> SoftCurrencyText => softCurrencyText;

    [Bindable("HardCurrency")]
    public IReadOnlyReactiveProperty<string> HardCurrencyText => hardCurrencyText;

    [Bindable("Wool")]
    public IReadOnlyReactiveProperty<string> WoolText => woolText;

    [BindableCommand]
    public ICommand Warp { get; }

    public TopBarViewModel(TimeWarpService timeWarpService, WalletService walletService)
    {
        if (timeWarpService == null)
            throw new ArgumentNullException(nameof(timeWarpService));
        if (walletService == null)
            throw new ArgumentNullException(nameof(walletService));

        softCurrencyText = new ReactiveProperty<string>(string.Empty).AddTo(disposables);
        hardCurrencyText = new ReactiveProperty<string>(string.Empty).AddTo(disposables);
        woolText = new ReactiveProperty<string>(string.Empty).AddTo(disposables);
        WarpLabel = Observable.Return("Warp").ToReadOnlyReactiveProperty().AddTo(disposables);
        Warp = new UiCommand(() => timeWarpService.ApplyWarp(14400d));

        BindResourceSlot(walletService, SoftCurrencyResourceId, softCurrencyText);
        BindResourceSlot(walletService, HardCurrencyResourceId, hardCurrencyText);
        BindResourceSlot(walletService, woolResourceId, woolText);
    }

    private void BindResourceSlot(
        WalletService walletService,
        string resourceId,
        ReactiveProperty<string> target
    )
    {
        if (!walletService.TryGetResourceDefinition(resourceId, out var definition))
            throw new InvalidOperationException(
                $"TopBarViewModel: Unknown resource '{resourceId}'. Check top bar configuration and content."
            );

        walletService
            .GetBalanceProperty(resourceId)
            .Subscribe(amount =>
                target.Value = ResourceTextFormatter.FormatResource(definition, amount)
            )
            .AddTo(disposables);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
