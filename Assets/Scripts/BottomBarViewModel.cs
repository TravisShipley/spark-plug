public sealed class BottomBarViewModel
{
    public UiCommand ShowUpgrades { get; }
    public UiCommand ShowManagers { get; }
    public UiCommand ShowStore { get; }

    public BottomBarViewModel(UiScreenService uiScreenService)
    {
        ShowUpgrades = new UiCommand(uiScreenService.ShowUpgrades);
        ShowManagers = new UiCommand(() => uiScreenService.ShowById("Managers"));
        ShowStore = new UiCommand(() => uiScreenService.ShowById("Store"));
    }
}
