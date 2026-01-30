public readonly struct UiBindingsContext
{
    public readonly WalletService WalletService;
    public readonly WalletViewModel WalletViewModel;
    public readonly UpgradeService UpgradeService;
    public readonly ModalService ModalService;
    public readonly UiServiceRegistry UiServices;

    public UiBindingsContext(
        ModalService modalService,
        UiServiceRegistry uiServices,
        UpgradeService upgradeService,
        WalletService walletService,
        WalletViewModel walletViewModel)
    {
        ModalService = modalService;
        UiServices = uiServices;
        UpgradeService = upgradeService;
        WalletService = walletService;
        WalletViewModel = walletViewModel;
    }
}