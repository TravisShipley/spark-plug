using System;
using System.Collections.Generic;
using System.Linq;

public sealed class NodeInstanceCatalog
{
    private readonly Dictionary<string, NodeInstanceDefinition> byId;
    public IReadOnlyList<NodeInstanceDefinition> NodeInstances { get; }

    public NodeInstanceCatalog(IEnumerable<NodeInstanceDefinition> nodeInstances)
    {
        var list = (nodeInstances ?? Enumerable.Empty<NodeInstanceDefinition>()).ToList();

        // Normalize: trim ids, preserve input order, and fail loud on duplicates.
        byId = new Dictionary<string, NodeInstanceDefinition>(StringComparer.Ordinal);
        var ordered = new List<NodeInstanceDefinition>();

        foreach (var nodeInstance in list)
        {
            if (nodeInstance == null)
                continue;

            var id = (nodeInstance.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
                continue;

            if (!byId.TryAdd(id, nodeInstance))
                throw new InvalidOperationException(
                    $"NodeInstanceCatalog: Duplicate node instance id '{id}'."
                );

            ordered.Add(nodeInstance);
        }

        NodeInstances = ordered;
    }

    public bool TryGet(string id, out NodeInstanceDefinition nodeInstance)
    {
        id = (id ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(id))
        {
            nodeInstance = null;
            return false;
        }

        return byId.TryGetValue(id, out nodeInstance);
    }

    public NodeInstanceDefinition GetRequired(string id)
    {
        if (!TryGet(id, out var nodeInstance) || nodeInstance == null)
        {
            throw new KeyNotFoundException(
                $"NodeInstanceCatalog: No node instance found for id '{id}'."
            );
        }

        return nodeInstance;
    }

    public IReadOnlyList<NodeInstanceDefinition> GetForZone(string zoneId)
    {
        var zone = (zoneId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(zone))
            return NodeInstances;

        return NodeInstances
            .Where(i =>
                string.Equals((i.zoneId ?? string.Empty).Trim(), zone, StringComparison.Ordinal)
            )
            .ToList();
    }

    public IReadOnlyList<NodeInstanceDefinition> GetForNode(string nodeId)
    {
        var node = (nodeId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(node))
            return Array.Empty<NodeInstanceDefinition>();

        return NodeInstances
            .Where(i =>
                string.Equals((i.nodeId ?? string.Empty).Trim(), node, StringComparison.Ordinal)
            )
            .ToList();
    }
}
