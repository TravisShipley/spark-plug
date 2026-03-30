public readonly struct UiBindingsContext
{
    public readonly TimeWarpService TimeWarpService;
    public readonly WalletService WalletService;
    public readonly WalletViewModel WalletViewModel;
    public readonly UpgradeService UpgradeService;
    public readonly BuyModeService BuyModeService;
    public readonly UiScreenService UiScreenService;
    public readonly UiServiceRegistry UiServices;
    public readonly IStateVarService StateVarService;

    public UiBindingsContext(
        UiScreenService uiScreenService,
        UiServiceRegistry uiServices,
        UpgradeService upgradeService,
        BuyModeService buyModeService,
        TimeWarpService timeWarpService,
        WalletService walletService,
        WalletViewModel walletViewModel,
        IStateVarService stateVarService
    )
    {
        UiScreenService = uiScreenService;
        UiServices = uiServices;
        UpgradeService = upgradeService;
        BuyModeService = buyModeService;
        TimeWarpService = timeWarpService;
        WalletService = walletService;
        WalletViewModel = walletViewModel;
        StateVarService = stateVarService;
    }
}
