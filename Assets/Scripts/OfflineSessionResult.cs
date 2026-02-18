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

    public long secondsAway;
    public List<ResourceGain> resourceGains = new();

    public IReadOnlyList<ResourceGain> ResourceGains => resourceGains;

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

    public bool HasMeaningfulGain(double epsilon = 0.0000001d)
    {
        return TotalGain() > epsilon;
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
