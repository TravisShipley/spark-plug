using System;
using System.Collections.Generic;
using System.Linq;

public sealed class BuyModeCatalog
{
    private static readonly BuyModeDefinition FallbackDefault = new BuyModeDefinition
    {
        id = "buy.x1",
        displayName = "x1",
        kind = "fixed",
        fixedCount = 1,
        tags = Array.Empty<string>(),
    };

    private readonly Dictionary<string, BuyModeDefinition> byId = new(StringComparer.Ordinal);

    public IReadOnlyList<BuyModeDefinition> All { get; }

    public BuyModeCatalog(IEnumerable<BuyModeDefinition> buyModes)
    {
        var ordered = new List<BuyModeDefinition>();
        var source = buyModes ?? Enumerable.Empty<BuyModeDefinition>();

        foreach (var buyMode in source)
        {
            if (buyMode == null)
                continue;

            var id = NormalizeId(buyMode.id);
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException("BuyModeCatalog: buy mode id is empty.");

            if (byId.ContainsKey(id))
                throw new InvalidOperationException($"BuyModeCatalog: duplicate buy mode id '{id}'.");

            byId[id] = buyMode;
            ordered.Add(buyMode);
        }

        All = ordered;
    }

    public bool TryGet(string id, out BuyModeDefinition definition)
    {
        var key = NormalizeId(id);
        if (string.IsNullOrEmpty(key))
        {
            definition = null;
            return false;
        }

        return byId.TryGetValue(key, out definition);
    }

    public BuyModeDefinition GetDefault()
    {
        if (All.Count > 0 && All[0] != null)
            return All[0];

        return CloneFallback();
    }

    private static BuyModeDefinition CloneFallback()
    {
        return new BuyModeDefinition
        {
            id = FallbackDefault.id,
            displayName = FallbackDefault.displayName,
            kind = FallbackDefault.kind,
            fixedCount = FallbackDefault.fixedCount,
            tags = Array.Empty<string>(),
        };
    }

    private static string NormalizeId(string raw)
    {
        return (raw ?? string.Empty).Trim();
    }
}
