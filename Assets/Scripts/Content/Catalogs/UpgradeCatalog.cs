using System;
using System.Collections.Generic;
using System.Linq;

public sealed class UpgradeCatalog
{
    private readonly Dictionary<string, UpgradeDefinition> byId;
    public IReadOnlyList<UpgradeDefinition> Upgrades { get; }

    public UpgradeCatalog(IEnumerable<UpgradeDefinition> upgrades)
    {
        var list = (upgrades ?? Enumerable.Empty<UpgradeDefinition>()).ToList();
        // Normalize: trim ids and dedupe (first wins)
        byId = new Dictionary<string, UpgradeDefinition>(StringComparer.Ordinal);
        foreach (var u in list)
        {
            if (u == null)
                continue;
            var id = (u.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
                continue;
            if (!byId.ContainsKey(id))
                byId[id] = u;
        }

        Upgrades = byId.Values.ToList();
    }

    public bool TryGet(string id, out UpgradeDefinition entry)
    {
        id = (id ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(id))
        {
            entry = null;
            return false;
        }

        return byId.TryGetValue(id, out entry);
    }

    public UpgradeDefinition GetRequired(string id)
    {
        if (!TryGet(id, out var e) || e == null)
            throw new KeyNotFoundException($"UpgradeCatalog: No upgrade found for id '{id}'.");
        return e;
    }

}
