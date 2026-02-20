using System;
using System.Collections.Generic;

public sealed class ManagersScreenViewModel : IDisposable
{
    private readonly List<UpgradeEntryViewModel> entries;

    public IReadOnlyList<UpgradeEntryViewModel> Entries => entries;

    public ManagersScreenViewModel(UpgradeListBuilder projectionService)
    {
        if (projectionService == null)
            throw new ArgumentNullException(nameof(projectionService));

        entries = new List<UpgradeEntryViewModel>(
            projectionService.BuildEntries(IsAutomationUpgrade)
        );
    }

    public void Dispose()
    {
        for (int i = 0; i < entries.Count; i++)
            entries[i]?.Dispose();

        entries.Clear();
    }

    private static bool IsAutomationUpgrade(UpgradeEntry upgrade)
    {
        var category = (upgrade?.category ?? string.Empty).Trim();
        return string.Equals(category, "Automation", StringComparison.OrdinalIgnoreCase);
    }
}
