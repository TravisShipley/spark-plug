using System;
using System.Collections.Generic;

public sealed class UpgradesScreenViewModel : IDisposable
{
    private readonly List<UpgradeEntryViewModel> entries;

    public IReadOnlyList<UpgradeEntryViewModel> Entries => entries;

    public UpgradesScreenViewModel(UpgradeListBuilder projectionService)
    {
        if (projectionService == null)
            throw new ArgumentNullException(nameof(projectionService));

        entries = new List<UpgradeEntryViewModel>(projectionService.BuildEntries());
    }

    public void Dispose()
    {
        for (int i = 0; i < entries.Count; i++)
            entries[i]?.Dispose();

        entries.Clear();
    }
}
