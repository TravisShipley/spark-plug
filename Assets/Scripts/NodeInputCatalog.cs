using System;
using System.Collections.Generic;

public sealed class NodeInputCatalog
{
    private readonly List<NodeInputDefinition> allInputs = new();
    private readonly Dictionary<string, List<NodeInputDefinition>> byNodeId = new(
        StringComparer.Ordinal
    );

    public NodeInputCatalog(GameDefinition definition)
    {
        if (definition == null)
            return;

        AddFromTopLevelTable(definition.nodeInputs);
        AddFromNodeLocalInputs(definition.nodes);
    }

    public IReadOnlyList<NodeInputDefinition> NodeInputs => allInputs;

    public int Count => allInputs.Count;

    public IEnumerable<string> NodeIdsWithInputs => byNodeId.Keys;

    public IReadOnlyList<NodeInputDefinition> GetForNode(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return Array.Empty<NodeInputDefinition>();

        var normalized = nodeId.Trim();
        if (byNodeId.TryGetValue(normalized, out var entries) && entries != null)
            return entries;

        return Array.Empty<NodeInputDefinition>();
    }

    private void AddFromTopLevelTable(IReadOnlyList<NodeInputDefinition> rows)
    {
        if (rows == null)
            return;

        for (int i = 0; i < rows.Count; i++)
            Add(rows[i], null);
    }

    private void AddFromNodeLocalInputs(IReadOnlyList<NodeDefinition> nodes)
    {
        if (nodes == null)
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var nodeId = (node?.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(nodeId) || node.inputs == null)
                continue;

            for (int j = 0; j < node.inputs.Count; j++)
                Add(node.inputs[j], nodeId);
        }
    }

    private void Add(NodeInputDefinition input, string fallbackNodeId)
    {
        if (input == null)
            return;

        var nodeId = (input.nodeId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(nodeId))
            nodeId = fallbackNodeId;
        if (string.IsNullOrEmpty(nodeId))
            return;

        var normalized = CloneWithNodeId(input, nodeId);
        allInputs.Add(normalized);

        if (!byNodeId.TryGetValue(nodeId, out var entries) || entries == null)
        {
            entries = new List<NodeInputDefinition>();
            byNodeId[nodeId] = entries;
        }

        entries.Add(normalized);
    }

    private static NodeInputDefinition CloneWithNodeId(NodeInputDefinition source, string nodeId)
    {
        return new NodeInputDefinition
        {
            nodeId = nodeId,
            resource = source.resource,
            amountPerCycle = source.amountPerCycle,
            amountPerCycleFromVar = source.amountPerCycleFromVar,
            amountPerCycleFromState = source.amountPerCycleFromState,
        };
    }
}
