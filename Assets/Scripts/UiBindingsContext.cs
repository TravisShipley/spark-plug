public readonly struct UiBindingsContext
{
    public readonly WalletService WalletService;
    public readonly WalletViewModel WalletViewModel;
    public readonly UpgradeService UpgradeService;
    public readonly UiScreenService UiScreenService;
    public readonly UiServiceRegistry UiServices;

    public UiBindingsContext(
        UiScreenService uiScreenService,
        UiServiceRegistry uiServices,
        UpgradeService upgradeService,
        WalletService walletService,
        WalletViewModel walletViewModel)
    {
        UiScreenService = uiScreenService;
        UiServices = uiServices;
        UpgradeService = upgradeService;
        WalletService = walletService;
        WalletViewModel = walletViewModel;
    }
}
