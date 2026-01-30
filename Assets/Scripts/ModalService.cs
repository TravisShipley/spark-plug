using System;

/// <summary>
/// Domain-facing API for opening/closing modals without leaking ModalManager + string IDs
/// throughout the project.
///
/// ModalManager remains the UI infrastructure (stacking, sorting, prefab instantiation).
/// ModalService is the intent-based entry point ("show upgrades", "close top", etc.).
/// </summary>
public sealed class ModalService
{
    private readonly ModalManager modalManager;

    public ModalService(ModalManager modalManager)
    {
        this.modalManager = modalManager ?? throw new ArgumentNullException(nameof(modalManager));
    }

    // Generic helpers (optional, but handy)
    public void ShowById(string id) => modalManager.ShowById(id);
    public void Show(string id, object payload) => modalManager.Show(id, payload);
    public void CloseTop() => modalManager.CloseTop();

    // Intent-based API (this is the real value)
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
