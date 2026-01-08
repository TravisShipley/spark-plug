using UniRx;
using System;

public class WalletService : IDisposable
{
    public ReactiveProperty<double> CashBalance { get; } = new ReactiveProperty<double>(0);
    public ReactiveProperty<double> GoldBalance { get; } = new ReactiveProperty<double>(0);

    private readonly CompositeDisposable disposables = new();

    public WalletService()
    {
        var data = SaveSystem.LoadGame();
        if (data != null)
        {
            CashBalance.Value = data.CashAmount;
            GoldBalance.Value = data.GoldAmount;
        }

        Observable.Merge(
                CashBalance.Skip(1).AsUnitObservable(),
                GoldBalance.Skip(1).AsUnitObservable()
            )
            .Throttle(TimeSpan.FromMilliseconds(250))
            .Subscribe(_ => Save())
            .AddTo(disposables);

        EventSystem.OnIncrementBalance
            .Subscribe(tuple => IncrementBalance(tuple.type, tuple.amount))
            .AddTo(disposables);
    }

    private void Save()
    {
        // Merge currency into existing save so generator state is preserved
        var data = SaveSystem.LoadGame() ?? new GameData();

        data.CashAmount = CashBalance.Value;
        data.GoldAmount = GoldBalance.Value;

        // Ensure generator list exists for newer save schema
        data.Generators ??= new System.Collections.Generic.List<GameData.GeneratorStateData>();

        SaveSystem.SaveGame(data);
    }

    public void IncrementBalance(CurrencyType currency, double amount)
    {
        var prop = GetCurrency(currency);
        prop.Value += amount;
    }

    private ReactiveProperty<double> GetCurrency(CurrencyType currency) =>
        currency == CurrencyType.Cash ? CashBalance : GoldBalance;

    public void Dispose()
    {
        disposables.Dispose();
    }
}