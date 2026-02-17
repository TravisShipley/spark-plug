using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class GameData : ISerializationCallbackReceiver
{
    [Serializable]
    public class ResourceBalanceData
    {
        public string ResourceId;
        public double Amount;
    }

    public double globalIncomeMultiplier = 1.0;

    [Serializable]
    public class GeneratorStateData
    {
        public string Id;
        public bool IsOwned;
        public bool IsAutomated;
        public int Level;
    }

    [Serializable]
    public class UpgradeStateData
    {
        // Stable upgrade identifier (matches UpgradeEntry.id in game definition content)
        public string Id;

        // Number of times purchased. For one-time upgrades, this will be 0 or 1.
        public int PurchasedCount;
    }

    public List<GeneratorStateData> Generators = new();
    public List<UpgradeStateData> Upgrades = new();
    public List<ResourceBalanceData> Resources = new();

    // Runtime lookup of one-time milestones that have already fired.
    [NonSerialized]
    public HashSet<string> FiredMilestoneIds = new(StringComparer.Ordinal);

    // Runtime lookup for unlocked node instances.
    [NonSerialized]
    public HashSet<string> UnlockedNodeInstanceIds = new(StringComparer.Ordinal);

    // Serialized bridge for JsonUtility (HashSet is not serialized directly).
    [SerializeField]
    private List<string> firedMilestoneIds = new();

    [SerializeField]
    private List<string> unlockedNodeInstanceIds = new();

    public void EnsureInitialized()
    {
        Generators ??= new List<GeneratorStateData>();
        Upgrades ??= new List<UpgradeStateData>();
        Resources ??= new List<ResourceBalanceData>();
        FiredMilestoneIds ??= new HashSet<string>(StringComparer.Ordinal);
        UnlockedNodeInstanceIds ??= new HashSet<string>(StringComparer.Ordinal);
        firedMilestoneIds ??= new List<string>();
        unlockedNodeInstanceIds ??= new List<string>();

        if (FiredMilestoneIds.Count == 0 && firedMilestoneIds.Count > 0)
        {
            for (int i = 0; i < firedMilestoneIds.Count; i++)
            {
                var normalized = (firedMilestoneIds[i] ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(normalized))
                    FiredMilestoneIds.Add(normalized);
            }
        }

        if (UnlockedNodeInstanceIds.Count == 0 && unlockedNodeInstanceIds.Count > 0)
        {
            for (int i = 0; i < unlockedNodeInstanceIds.Count; i++)
            {
                var normalized = (unlockedNodeInstanceIds[i] ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(normalized))
                    UnlockedNodeInstanceIds.Add(normalized);
            }
        }
    }

    public void OnBeforeSerialize()
    {
        EnsureInitialized();
        firedMilestoneIds.Clear();

        foreach (var id in FiredMilestoneIds)
        {
            var normalized = (id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized))
                continue;

            firedMilestoneIds.Add(normalized);
        }

        firedMilestoneIds.Sort(StringComparer.Ordinal);

        unlockedNodeInstanceIds.Clear();
        foreach (var id in UnlockedNodeInstanceIds)
        {
            var normalized = (id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized))
                continue;

            unlockedNodeInstanceIds.Add(normalized);
        }

        unlockedNodeInstanceIds.Sort(StringComparer.Ordinal);
    }

    public void OnAfterDeserialize()
    {
        EnsureInitialized();
        FiredMilestoneIds.Clear();

        for (int i = 0; i < firedMilestoneIds.Count; i++)
        {
            var normalized = (firedMilestoneIds[i] ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized))
                continue;

            FiredMilestoneIds.Add(normalized);
        }

        UnlockedNodeInstanceIds.Clear();
        for (int i = 0; i < unlockedNodeInstanceIds.Count; i++)
        {
            var normalized = (unlockedNodeInstanceIds[i] ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized))
                continue;

            UnlockedNodeInstanceIds.Add(normalized);
        }
    }
}
