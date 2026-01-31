using System;
using UniRx;

public class WalletService : IDisposable
{
    public ReactiveProperty<double> CashBalance { get; } = new ReactiveProperty<double>(0);
    public ReactiveProperty<double> GoldBalance { get; } = new ReactiveProperty<double>(0);

    private readonly CompositeDisposable disposables = new();
    private readonly SaveService saveService;

    public WalletService(SaveService saveService)
    {
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));

        var data = this.saveService.Data;
        if (data != null)
        {
            CashBalance.Value = data.CashAmount;
            GoldBalance.Value = data.GoldAmount;
        }

        Observable
            .Merge(CashBalance.Skip(1).AsUnitObservable(), GoldBalance.Skip(1).AsUnitObservable())
            .Subscribe(_ => PersistToSave())
            .AddTo(disposables);

        EventSystem
            .OnIncrementBalance.Subscribe(tuple => IncrementBalance(tuple.type, tuple.amount))
            .AddTo(disposables);
    }

    private void PersistToSave()
    {
        var data = saveService.Data;
        if (data == null)
            return;

        data.CashAmount = CashBalance.Value;
        data.GoldAmount = GoldBalance.Value;

        saveService.RequestSave();
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
