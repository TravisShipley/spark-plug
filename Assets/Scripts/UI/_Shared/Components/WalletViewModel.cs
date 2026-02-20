using UniRx;

public class WalletViewModel
{
    private readonly WalletService walletService;

    private CompositeDisposable disposables = new CompositeDisposable();

    public WalletViewModel(WalletService walletService)
    {
        this.walletService = walletService;

        // If you need any VM-specific subscriptions, do them here and AddTo(disposables).
        // The WalletService already handles persistence & event subscriptions.
    }

    public IReadOnlyReactiveProperty<double> Balance(string resourceId)
    {
        return walletService.GetBalanceProperty(resourceId);
    }

    public double GetBalance(string resourceId)
    {
        return walletService.GetBalance(resourceId);
    }

    public void Add(string resourceId, double amount)
    {
        walletService.Add(resourceId, amount);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
