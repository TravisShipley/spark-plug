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

    [Serializable]
    public class LifetimeEarningData
    {
        public string ResourceId;
        public double Amount;
    }

    [Serializable]
    public class GeneratorStateData
    {
        public string Id;
        public bool IsOwned;
        public bool IsEnabled;

        // Legacy compatibility for older saves.
        public bool IsAutomated;
        public bool IsAutomationPurchased;
        public int Level;
    }

    [Serializable]
    public class UpgradeStateData
    {
        // Stable upgrade identifier (matches UpgradeDefinition.id in game definition content)
        public string Id;

        // Number of times purchased. For one-time upgrades, this will be 0 or 1.
        public int PurchasedCount;
    }

    public List<GeneratorStateData> Generators = new();
    public List<UpgradeStateData> Upgrades = new();
    public List<ResourceBalanceData> Resources = new();
    public List<LifetimeEarningData> LifetimeEarnings = new();
    public string ActiveBuffId;
    public long ActiveBuffExpiresAtUnixSeconds;
    public long lastSeenUnixSeconds;

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
        LifetimeEarnings ??= new List<LifetimeEarningData>();
        ActiveBuffId = (ActiveBuffId ?? string.Empty).Trim();
        if (ActiveBuffExpiresAtUnixSeconds < 0)
            ActiveBuffExpiresAtUnixSeconds = 0;
        if (string.IsNullOrEmpty(ActiveBuffId))
            ActiveBuffExpiresAtUnixSeconds = 0;
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

        NormalizeGeneratorStates();
    }

    public void OnBeforeSerialize()
    {
        EnsureInitialized();
        NormalizeGeneratorStates();
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

    private void NormalizeGeneratorStates()
    {
        if (Generators == null)
            return;

        for (int i = 0; i < Generators.Count; i++)
        {
            var state = Generators[i];
            if (state == null)
                continue;

            state.Id = (state.Id ?? string.Empty).Trim();
            state.IsAutomationPurchased = state.IsAutomationPurchased || state.IsAutomated;

            if (state.IsOwned)
                state.IsEnabled = true;

            if (state.IsAutomationPurchased)
            {
                state.IsOwned = true;
                state.IsEnabled = true;
            }

            if (state.IsOwned && state.Level < 1)
                state.Level = 1;

            if (!state.IsOwned && state.Level < 0)
                state.Level = 0;

            if (!state.IsOwned)
                state.Level = 0;

            // Write legacy field for forward/backward compatibility.
            state.IsAutomated = state.IsAutomationPurchased;
        }
    }
}
