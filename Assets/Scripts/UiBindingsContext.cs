public readonly struct UiBindingsContext
{
    public readonly WalletService Wallet;
    public readonly WalletViewModel WalletVM;
    public readonly UpgradeService Upgrades;
    public readonly ModalService Modals;
    public readonly UiServiceRegistry UiService;

    public UiBindingsContext(
        ModalService modals,
        UiServiceRegistry uiService,
        UpgradeService upgrades,
        WalletService wallet,
        WalletViewModel walletVM)
    {
        Modals = modals;
        UiService = uiService;
        Upgrades = upgrades;
        Wallet = wallet;
        WalletVM = walletVM;
    }
}