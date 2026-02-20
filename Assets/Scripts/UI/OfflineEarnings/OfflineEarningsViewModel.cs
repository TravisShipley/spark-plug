using System;
using UniRx;

public sealed class OfflineEarningsViewModel : IDisposable
{
    private readonly CompositeDisposable disposables = new();
    private readonly WalletService walletService;
    private readonly Action closeScreen;
    private readonly OfflineSessionResult result;
    private readonly ReactiveProperty<bool> canCollect;

    public IReadOnlyReactiveProperty<string> Title { get; }
    public IReadOnlyReactiveProperty<string> Summary { get; }
    public IReadOnlyReactiveProperty<string> EarningsLine { get; }
    public UiCommand Collect { get; }

    public OfflineEarningsViewModel(
        OfflineSessionResult result,
        WalletService walletService,
        Action closeScreen
    )
    {
        this.result = result ?? new OfflineSessionResult();
        this.walletService =
            walletService ?? throw new ArgumentNullException(nameof(walletService));
        this.closeScreen = closeScreen ?? throw new ArgumentNullException(nameof(closeScreen));

        var totalGain = this.result.TotalGain();
        var canCollectNow = this.result.secondsAway > 0 && totalGain > 0d;

        canCollect = new ReactiveProperty<bool>(canCollectNow).AddTo(disposables);
        Title = Observable
            .Return("Offline Earnings")
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);
        Summary = Observable
            .Return($"Away for {FormatDuration(this.result.secondsAway)}.")
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);
        EarningsLine = Observable
            .Return(Format.Currency(totalGain))
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        Collect = new UiCommand(ExecuteCollect, canCollect);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }

    private void ExecuteCollect()
    {
        if (!canCollect.Value)
            return;

        walletService.ApplyOfflineEarnings(result);
        canCollect.Value = false;
        closeScreen.Invoke();
    }

    private static string FormatDuration(long seconds)
    {
        var clamped = Math.Max(0, seconds);
        var time = TimeSpan.FromSeconds(clamped);

        if (time.TotalHours >= 1d)
            return $"{(int)time.TotalHours}h {time.Minutes}m";
        if (time.TotalMinutes >= 1d)
            return $"{time.Minutes}m {time.Seconds}s";

        return $"{time.Seconds}s";
    }
}
