using System;
using System.Collections.Generic;
using System.Linq;

public sealed class BuffCatalog
{
    private readonly Dictionary<string, BuffDefinition> byId = new(StringComparer.Ordinal);

    public IReadOnlyList<BuffDefinition> Buffs { get; }

    public BuffCatalog(IEnumerable<BuffDefinition> buffs)
    {
        var ordered = new List<BuffDefinition>();
        var source = buffs ?? Enumerable.Empty<BuffDefinition>();

        foreach (var buff in source)
        {
            if (buff == null)
                continue;

            var id = NormalizeId(buff.id);
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException("BuffCatalog: buff id is empty.");

            if (byId.ContainsKey(id))
                throw new InvalidOperationException($"BuffCatalog: duplicate buff id '{id}'.");

            if (buff.durationSeconds <= 0)
            {
                throw new InvalidOperationException(
                    $"BuffCatalog: buff '{id}' durationSeconds must be > 0."
                );
            }

            // Buff stacking policy is validated at import/definition validation time (GameDefinitionValidator).
            // BuffCatalog should not maintain a duplicate list of valid stacking strings.
            var stacking = NormalizeId(buff.stacking);

            if (buff.effects == null || buff.effects.Length == 0)
            {
                throw new InvalidOperationException(
                    $"BuffCatalog: buff '{id}' has no effects[].modifierId entries."
                );
            }

            byId[id] = buff;
            ordered.Add(buff);
        }

        ordered.Sort(
            (a, b) =>
                string.Compare(NormalizeId(a?.id), NormalizeId(b?.id), StringComparison.Ordinal)
        );

        Buffs = ordered;
    }

    public BuffDefinition Get(string id)
    {
        var key = NormalizeId(id);
        if (string.IsNullOrEmpty(key) || !byId.TryGetValue(key, out var buff) || buff == null)
            throw new KeyNotFoundException($"BuffCatalog: no buff found for id '{id}'.");

        return buff;
    }

    public bool TryGet(string id, out BuffDefinition buff)
    {
        var key = NormalizeId(id);
        if (string.IsNullOrEmpty(key))
        {
            buff = null;
            return false;
        }

        return byId.TryGetValue(key, out buff);
    }

    private static string NormalizeId(string raw)
    {
        return (raw ?? string.Empty).Trim();
    }
}
