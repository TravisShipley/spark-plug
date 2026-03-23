using System;
using System.Collections.Generic;

[Serializable]
public sealed class OfflineSessionResult
{
    [Serializable]
    public sealed class ResourceGain
    {
        public string resourceId;
        public double amount;
    }

    [Serializable]
    public sealed class GeneratorStateUpdate
    {
        public string nodeInstanceId;
        public bool wasRunning;
        public bool hasPendingPayout;
        public double cycleElapsedSeconds;
        public double pendingPayout;
    }

    public long secondsAway;
    public List<ResourceGain> resourceGains = new();
    public List<GeneratorStateUpdate> generatorStateUpdates = new();

    public IReadOnlyList<ResourceGain> ResourceGains => resourceGains;
    public IReadOnlyList<GeneratorStateUpdate> GeneratorStateUpdates => generatorStateUpdates;

    public void AddGain(string resourceId, double amount)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0d)
            return;

        var id = NormalizeResourceId(resourceId);
        if (string.IsNullOrEmpty(id))
            return;

        resourceGains ??= new List<ResourceGain>();
        for (int i = 0; i < resourceGains.Count; i++)
        {
            var entry = resourceGains[i];
            if (entry == null)
                continue;

            if (!string.Equals(NormalizeResourceId(entry.resourceId), id, StringComparison.Ordinal))
                continue;

            entry.amount += amount;
            return;
        }

        resourceGains.Add(new ResourceGain { resourceId = id, amount = amount });
    }

    public void SetGeneratorState(
        string nodeInstanceId,
        bool wasRunning,
        bool hasPendingPayout,
        double cycleElapsedSeconds,
        double pendingPayout
    )
    {
        var id = NormalizeResourceId(nodeInstanceId);
        if (string.IsNullOrEmpty(id))
            return;

        generatorStateUpdates ??= new List<GeneratorStateUpdate>();
        for (int i = 0; i < generatorStateUpdates.Count; i++)
        {
            var entry = generatorStateUpdates[i];
            if (entry == null)
                continue;

            if (!string.Equals(NormalizeResourceId(entry.nodeInstanceId), id, StringComparison.Ordinal))
                continue;

            entry.wasRunning = wasRunning;
            entry.hasPendingPayout = hasPendingPayout;
            entry.cycleElapsedSeconds = cycleElapsedSeconds;
            entry.pendingPayout = pendingPayout;
            return;
        }

        generatorStateUpdates.Add(
            new GeneratorStateUpdate
            {
                nodeInstanceId = id,
                wasRunning = wasRunning,
                hasPendingPayout = hasPendingPayout,
                cycleElapsedSeconds = cycleElapsedSeconds,
                pendingPayout = pendingPayout,
            }
        );
    }

    public bool HasMeaningfulGain(double epsilon = 0.0000001d)
    {
        return TotalGain() > epsilon;
    }

    public bool HasGeneratorStateChanges()
    {
        return generatorStateUpdates != null && generatorStateUpdates.Count > 0;
    }

    public double TotalGain()
    {
        if (resourceGains == null || resourceGains.Count == 0)
            return 0d;

        double total = 0d;
        for (int i = 0; i < resourceGains.Count; i++)
        {
            var entry = resourceGains[i];
            if (entry == null)
                continue;

            if (double.IsNaN(entry.amount) || double.IsInfinity(entry.amount) || entry.amount <= 0d)
                continue;

            total += entry.amount;
        }

        return total;
    }

    public double TotalGainFor(string resourceId)
    {
        var id = NormalizeResourceId(resourceId);
        if (string.IsNullOrEmpty(id) || resourceGains == null)
            return 0d;

        double total = 0d;
        for (int i = 0; i < resourceGains.Count; i++)
        {
            var entry = resourceGains[i];
            if (entry == null)
                continue;

            if (!string.Equals(NormalizeResourceId(entry.resourceId), id, StringComparison.Ordinal))
                continue;

            if (double.IsNaN(entry.amount) || double.IsInfinity(entry.amount) || entry.amount <= 0d)
                continue;

            total += entry.amount;
        }

        return total;
    }

    private static string NormalizeResourceId(string resourceId)
    {
        return (resourceId ?? string.Empty).Trim();
    }
}
