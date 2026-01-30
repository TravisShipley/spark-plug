public sealed class BottomBarViewModel
{
    public UiCommand ShowUpgrades { get; }
    public UiCommand ShowManagers { get; }
    public UiCommand ShowStore { get; }

    public BottomBarViewModel(ModalService modals)
    {
        ShowUpgrades = new UiCommand(modals.ShowUpgrades);
        ShowManagers = new UiCommand(() => modals.ShowById("Managers"));
        ShowStore = new UiCommand(() => modals.ShowById("Store"));
    }
}