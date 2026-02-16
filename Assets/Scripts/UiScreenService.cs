using System;

public sealed class UiScreenService
{
    private readonly ModalManager modalManager;

    public UiScreenService(ModalManager modalManager)
    {
        this.modalManager = modalManager ?? throw new ArgumentNullException(nameof(modalManager));
    }

    // Generic helpers (optional, but handy)
    public void ShowById(string id) => modalManager.ShowById(id);

    public void Show(string id, object payload) => modalManager.Show(id, payload);

    public void CloseTop() => modalManager.CloseTop();

    public void ShowUpgrades() => modalManager.ShowById("UPGRADES");

    public void ShowManagers() => modalManager.ShowById("MANAGERS");

    public void ShowStore() => modalManager.ShowById("STORE");
}

// TODO
/*
modalService.Confirm(
  title: "Reset save?",
  message: "This cannot be undone.",
  confirmText: "Reset",
  cancelText: "Cancel",
  onConfirm: () => saveService.Reset()
);
*/
