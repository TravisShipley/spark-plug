using System;
using Ignition.Binding;
using UniRx;

public sealed class TimeWarpResultsScreenViewModel
{
    private readonly ReactiveProperty<string> name = new(string.Empty);
    private readonly ReactiveProperty<string> title = new(string.Empty);
    private readonly ReactiveProperty<string> resultText = new(string.Empty);

    [Bindable("Display Name")]
    public IReadOnlyReactiveProperty<string> Name => name;

    [Bindable]
    public IReadOnlyReactiveProperty<string> Title => title;

    [Bindable]
    public IReadOnlyReactiveProperty<string> ResultText => resultText;

    public TimeWarpResultsScreenViewModel(OfflineSessionResult result)
    {
        result ??= new OfflineSessionResult();
        var totalGain = result.TotalGain();

        title.Value = BuildTitle(result);
        resultText.Value = Format.Currency(totalGain);
        name.Value = "TimeWarpResultsScreenViewModel";
    }

    private static string BuildTitle(OfflineSessionResult result)
    {
        var duration = TimeFormat.FormatDuration(Math.Max(0L, result.secondsAway));
        return $"{duration} Profit.";
    }
}
