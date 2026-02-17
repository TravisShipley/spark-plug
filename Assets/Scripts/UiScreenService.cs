using System;

public sealed class UiScreenService
{
    private readonly UiScreenManager uiScreenManager;

    public UiScreenService(UiScreenManager uiScreenManager)
    {
        this.uiScreenManager =
            uiScreenManager ?? throw new ArgumentNullException(nameof(uiScreenManager));
    }

    // Generic helpers (optional, but handy)
    public void ShowById(string id) => uiScreenManager.ShowById(id);

    public void Show(string id, object payload) => uiScreenManager.Show(id, payload);

    public void CloseTop() => uiScreenManager.CloseTop();

    public void ShowUpgrades() => uiScreenManager.ShowById("UPGRADES");

    public void ShowManagers() => uiScreenManager.ShowById("MANAGERS");

    public void ShowStore() => uiScreenManager.ShowById("STORE");
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
