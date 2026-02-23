using System;
using System.Collections.Generic;

public sealed class UpgradesScreenViewModel : IDisposable
{
    private readonly List<UpgradeEntryViewModel> entries;

    public IReadOnlyList<UpgradeEntryViewModel> Entries => entries;

    public UpgradesScreenViewModel(UpgradeListBuilder builder)
    {
        if (builder == null)
            throw new ArgumentNullException(nameof(builder));

        entries = new List<UpgradeEntryViewModel>(builder.BuildEntries(IsRelevantUpgrade));
    }

    private static bool IsRelevantUpgrade(UpgradeDefinition upgrade)
    {
        var category = (upgrade?.category ?? string.Empty).Trim();
        return string.Equals(category, "node", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        for (int i = 0; i < entries.Count; i++)
            entries[i]?.Dispose();

        entries.Clear();
    }
}
