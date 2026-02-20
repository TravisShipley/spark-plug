using System.Collections.Generic;
using UniRx;
using UnityEngine;

public readonly struct GeneratorComposeContext
{
    public readonly GameObject GeneratorPrefab;
    public readonly Transform Container;

    public readonly WalletService WalletService;
    public readonly WalletViewModel WalletViewModel;
    public readonly TickService TickService;
    public readonly SaveService SaveService;
    public readonly UiServiceRegistry UiServices;
    public readonly UpgradeService UpgradeService;

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
        SaveService saveService,
        UiServiceRegistry uiServices,
        UpgradeService upgradeService,
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
        SaveService = saveService;
        UiServices = uiServices;
        UpgradeService = upgradeService;
        Disposables = disposables;
        Models = models;
        Services = services;
        ViewModels = viewModels;
    }
}
