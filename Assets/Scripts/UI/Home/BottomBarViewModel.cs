using System;
using UniRx;

public sealed class BottomBarViewModel
{
    public UiCommand ShowUpgrades { get; }
    public UiCommand ShowManagers { get; }
    public UiCommand ShowPrestige { get; }
    public UiCommand ShowStore { get; }
    public IReadOnlyReactiveProperty<bool> ShowUpgradesBadge { get; }
    public IReadOnlyReactiveProperty<bool> ShowManagersBadge { get; }

    public BottomBarViewModel(UiScreenService uiScreenService, UpgradeService upgradeService)
    {
        if (uiScreenService == null)
            throw new ArgumentNullException(nameof(uiScreenService));
        if (upgradeService == null)
            throw new ArgumentNullException(nameof(upgradeService));

        ShowUpgrades = new UiCommand(uiScreenService.ShowUpgrades);
        ShowManagers = new UiCommand(uiScreenService.ShowManagers);
        ShowPrestige = new UiCommand(uiScreenService.ShowPrestige);
        ShowStore = new UiCommand(uiScreenService.ShowStore);
        ShowUpgradesBadge = upgradeService.HasAffordableUpgrades;
        ShowManagersBadge = upgradeService.HasAffordableManagers;
    }
}
