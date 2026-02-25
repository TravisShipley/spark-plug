using System;
using UniRx;

public sealed class BuyModeViewModel : IDisposable
{
    private readonly CompositeDisposable disposables = new();

    public IReadOnlyReactiveProperty<string> Label { get; }
    public UiCommand CycleBuyModeCommand { get; }

    public BuyModeViewModel(BuyModeService buyModeService)
    {
        if (buyModeService == null)
            throw new ArgumentNullException(nameof(buyModeService));

        Label = buyModeService
            .SelectedBuyMode.Select(mode =>
            {
                if (mode == null)
                    return "x1";

                var displayName = (mode.displayName ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(displayName))
                    return displayName;

                var id = (mode.id ?? string.Empty).Trim();
                return string.IsNullOrEmpty(id) ? "x1" : id;
            })
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        CycleBuyModeCommand = new UiCommand(buyModeService.CycleNext);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
