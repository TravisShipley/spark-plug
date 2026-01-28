using UniRx;

public class WalletViewModel
{
    private readonly WalletService walletService;

    // Expose the service properties directly so UI can bind to them
    public ReactiveProperty<double> CashBalance => walletService.CashBalance;
    public ReactiveProperty<double> GoldBalance => walletService.GoldBalance;

    private CompositeDisposable disposables = new CompositeDisposable();

    public WalletViewModel(WalletService walletService)
    {
        this.walletService = walletService;

        // If you need any VM-specific subscriptions, do them here and AddTo(disposables).
        // The WalletService already handles persistence & event subscriptions.
    }

    // Forwarding method so views/controllers can change balances via the VM
    public void IncrementBalance(CurrencyType currency, double amount)
    {
        walletService.IncrementBalance(currency, amount);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}