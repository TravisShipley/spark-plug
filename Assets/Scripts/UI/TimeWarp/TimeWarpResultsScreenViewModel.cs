using System;
using Ignition.Binding;
using Ignition.Commands;
using UniRx;
using UnityEngine;

public sealed class TimeWarpResultsScreenViewModel
{
    private readonly ReactiveProperty<string> title = new(string.Empty);
    private readonly ReactiveProperty<string> resultText = new(string.Empty);

    [Bindable]
    public IReadOnlyReactiveProperty<string> Title => title;

    [Bindable]
    public IReadOnlyReactiveProperty<string> ResultText => resultText;

    public TimeWarpResultsScreenViewModel(OfflineSessionResult result)
    {
        result ??= new OfflineSessionResult();
        var totalGain = result.TotalGain();

        title.Value = "4h Profit";
        resultText.Value = Format.Currency(totalGain);
    }
}
