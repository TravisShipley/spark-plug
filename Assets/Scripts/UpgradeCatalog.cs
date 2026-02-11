using System;
using System.Collections.Generic;
using System.Linq;

public sealed class UpgradeCatalog
{
    private readonly Dictionary<string, UpgradeEntry> byId;
    public IReadOnlyList<UpgradeEntry> Upgrades { get; }

    public UpgradeCatalog(IEnumerable<UpgradeEntry> upgrades)
    {
        var list = (upgrades ?? Enumerable.Empty<UpgradeEntry>()).ToList();
        // Normalize: trim ids and dedupe (first wins)
        byId = new Dictionary<string, UpgradeEntry>(StringComparer.Ordinal);
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

    public bool TryGet(string id, out UpgradeEntry entry)
    {
        id = (id ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(id))
        {
            entry = null;
            return false;
        }

        return byId.TryGetValue(id, out entry);
    }

    public UpgradeEntry GetRequired(string id)
    {
        if (!TryGet(id, out var e) || e == null)
            throw new KeyNotFoundException($"UpgradeCatalog: No upgrade found for id '{id}'.");
        return e;
    }

    public List<UpgradeEntry> GetForGenerator(string generatorId)
    {
        var gen = (generatorId ?? string.Empty).Trim();
        var result = new List<UpgradeEntry>();
        foreach (var u in Upgrades)
        {
            var target = (u.generatorId ?? string.Empty).Trim();
            if (
                string.IsNullOrEmpty(target)
                || (
                    !string.IsNullOrEmpty(gen)
                    && string.Equals(target, gen, StringComparison.Ordinal)
                )
            )
                result.Add(u);
        }
        return result;
    }
}
