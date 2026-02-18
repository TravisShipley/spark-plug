using System;

public sealed class UiScreenService
{
    private readonly UiScreenManager uiScreenManager;
    private readonly WalletService walletService;

    public UiScreenService(UiScreenManager uiScreenManager, WalletService walletService)
    {
        this.uiScreenManager =
            uiScreenManager ?? throw new ArgumentNullException(nameof(uiScreenManager));
        this.walletService = walletService ?? throw new ArgumentNullException(nameof(walletService));
    }

    // Generic helpers (optional, but handy)
    public void ShowById(string id) => uiScreenManager.ShowById(id);

    public void Show(string id, object payload) => uiScreenManager.Show(id, payload);

    public void CloseTop() => uiScreenManager.CloseTop();

    public void ShowUpgrades() => uiScreenManager.Show("UPGRADES", uiScreenManager.UpgradesScreenViewModel);

    public void ShowManagers() => uiScreenManager.Show("MANAGERS", uiScreenManager.ManagersScreenViewModel);

    public void ShowStore() => uiScreenManager.ShowById("STORE");

    public void ShowOfflineEarnings(OfflineSessionResult result)
    {
        var viewModel = new OfflineEarningsViewModel(result, walletService, CloseTop);
        uiScreenManager.Show("OFFLINE_EARNINGS", viewModel);
    }
}

// TODO
/*
uiScreenService.Confirm(
  title: "Reset save?",
  message: "This cannot be undone.",
  confirmText: "Reset",
  cancelText: "Cancel",
  onConfirm: () => saveService.Reset()
);
*/
