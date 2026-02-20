using System;
using System.Collections.Generic;
using System.Linq;

public sealed class NodeCatalog
{
    private readonly Dictionary<string, NodeDefinition> byId;
    public IReadOnlyList<NodeDefinition> Nodes { get; }

    public NodeCatalog(IEnumerable<NodeDefinition> nodes)
    {
        var list = (nodes ?? Enumerable.Empty<NodeDefinition>()).ToList();

        // Normalize: trim ids, preserve input order, and fail loud on duplicates.
        byId = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal);
        var ordered = new List<NodeDefinition>();

        foreach (var node in list)
        {
            if (node == null)
                continue;

            var id = (node.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
                continue;

            if (!byId.TryAdd(id, node))
                throw new InvalidOperationException($"NodeCatalog: Duplicate node id '{id}'.");

            ordered.Add(node);
        }

        Nodes = ordered;
    }

    public bool TryGet(string id, out NodeDefinition node)
    {
        id = (id ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(id))
        {
            node = null;
            return false;
        }

        return byId.TryGetValue(id, out node);
    }

    public NodeDefinition GetRequired(string id)
    {
        if (!TryGet(id, out var node) || node == null)
            throw new KeyNotFoundException($"NodeCatalog: No node found for id '{id}'.");

        return node;
    }

    public IReadOnlyList<NodeDefinition> GetForZone(string zoneId)
    {
        var zone = (zoneId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(zone))
            return Nodes;

        return Nodes
            .Where(n =>
                string.Equals((n.zoneId ?? string.Empty).Trim(), zone, StringComparison.Ordinal)
            )
            .ToList();
    }

    public IReadOnlyList<NodeDefinition> GetByTag(string tag)
    {
        var normalizedTag = (tag ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalizedTag))
            return Array.Empty<NodeDefinition>();

        return Nodes
            .Where(n =>
                n.tags != null
                && n.tags.Any(t =>
                    string.Equals(t?.Trim(), normalizedTag, StringComparison.OrdinalIgnoreCase)
                )
            )
            .ToList();
    }
}
