using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Idle/Upgrade Database", fileName = "UpgradeDatabase")]
public class UpgradeDatabase : ScriptableObject
{
    [SerializeField, Tooltip("Imported from Sheet/CSV. Treat as read-only; re-import to edit.")]
    private List<UpgradeDefinition> upgrades = new();
    public IReadOnlyList<UpgradeDefinition> Upgrades => upgrades;

    private Dictionary<string, UpgradeDefinition> byId;

    private void OnEnable()
    {
        Normalize();
        RebuildIndex();
    }

    public bool TryGet(string id, out UpgradeDefinition upgrade)
    {
        EnsureIndex();
        return byId.TryGetValue(id, out upgrade);
    }

    public UpgradeDefinition GetRequired(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Upgrade id is null/empty.", nameof(id));

        EnsureIndex();

        id = id.Trim();
        if (!byId.TryGetValue(id, out var upgrade) || upgrade == null)
            throw new KeyNotFoundException($"UpgradeDatabase: No upgrade found for id '{id}'.");

        return upgrade;
    }

    public List<UpgradeDefinition> GetForGenerator(string generatorId)
    {
        var result = new List<UpgradeDefinition>();
        string genId = (generatorId ?? string.Empty).Trim();

        foreach (var u in upgrades)
        {
            if (u == null) continue;

            string targetId = (u.GeneratorId ?? string.Empty).Trim();

            // Global upgrade (no target)
            if (string.IsNullOrEmpty(targetId))
            {
                result.Add(u);
                continue;
            }

            // Per-generator upgrade
            if (!string.IsNullOrEmpty(genId) && string.Equals(targetId, genId, StringComparison.Ordinal))
                result.Add(u);
        }

        return result;
    }

    public void ReplaceAll(List<UpgradeDefinition> newList)
    {
        upgrades = newList ?? new List<UpgradeDefinition>();
        byId = null;
        Normalize();
        RebuildIndex();
    }

    private void EnsureIndex()
    {
        if (byId == null)
            RebuildIndex();
    }

    private void Normalize()
    {
        upgrades ??= new List<UpgradeDefinition>();

        // Remove nulls
        upgrades.RemoveAll(u => u == null);

        // De-duplicate by trimmed Id (keep first)
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = upgrades.Count - 1; i >= 0; i--)
        {
            var id = (upgrades[i].Id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id) || !seen.Add(id))
                upgrades.RemoveAt(i);
        }

        // Stable order for deterministic UI / diffs
        upgrades.Sort((a, b) => string.Compare((a.Id ?? string.Empty).Trim(), (b.Id ?? string.Empty).Trim(), StringComparison.Ordinal));
    }

    private void RebuildIndex()
    {
        upgrades ??= new List<UpgradeDefinition>();
        byId = new Dictionary<string, UpgradeDefinition>(StringComparer.Ordinal);

        foreach (var u in upgrades)
        {
            var id = (u.Id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
                continue;

            // Normalize() should have removed duplicates, but keep this guard in case the list was modified at runtime.
            if (!byId.ContainsKey(id))
                byId.Add(id, u);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        Normalize();
        byId = null;
    }
#endif
}