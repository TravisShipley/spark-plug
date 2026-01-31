public readonly struct GeneratorComposeContext
{
    public readonly GameObject GeneratorPrefab;
    public readonly Transform Container;

    public readonly WalletService WalletService;
    public readonly WalletViewModel WalletViewModel;
    public readonly TickService TickService;

    public readonly UiServiceRegistry UiServices;

    public readonly GameData GameData;
    public readonly UpgradeService UpgradeService;
    public readonly Subject<Unit> SaveRequests;
    public readonly CompositeDisposable Disposables;

    public readonly List<GeneratorModel> Models;
    public readonly List<GeneratorService> Services;
    public readonly List<GeneratorViewModel> ViewModels;

    public GeneratorComposeContext(
        GameObject generatorPrefab,
        Transform container,
        WalletService walletService,
        WalletViewModel walletViewModel,
        TickService tickService,
        UiServiceRegistry uiServices,
        GameData gameData,
        UpgradeService upgradeService,
        Subject<Unit> saveRequests,
        CompositeDisposable disposables,
        List<GeneratorModel> models,
        List<GeneratorService> services,
        List<GeneratorViewModel> viewModels
    )
    {
        GeneratorPrefab = generatorPrefab;
        Container = container;
        WalletService = walletService;
        WalletViewModel = walletViewModel;
        TickService = tickService;
        UiServices = uiServices;
        GameData = gameData;
        UpgradeService = upgradeService;
        SaveRequests = saveRequests;
        Disposables = disposables;
        Models = models;
        Services = services;
        ViewModels = viewModels;
    }
}
