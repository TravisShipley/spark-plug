using System;
using System.Collections.Generic;
using System.Linq;

public static class GameDefinitionValidator
{
    public static void Validate(GameDefinition gd)
    {
        if (gd == null) throw new ArgumentNullException(nameof(gd));

        // ---- Nodes
        var nodeIds = new HashSet<string>();
        foreach (var n in gd.nodes)
        {
            if (string.IsNullOrWhiteSpace(n.id))
                throw new InvalidOperationException("Node with empty id.");
            if (!nodeIds.Add(n.id))
                throw new InvalidOperationException($"Duplicate node id: {n.id}");
        }

        // ---- NodeInstances -> Nodes
        var instanceIds = new HashSet<string>();
        foreach (var i in gd.nodeInstances)
        {
            if (string.IsNullOrWhiteSpace(i.id))
                throw new InvalidOperationException("NodeInstance with empty id.");
            if (!instanceIds.Add(i.id))
                throw new InvalidOperationException($"Duplicate nodeInstance id: {i.id}");

            if (!nodeIds.Contains(i.nodeId))
                throw new InvalidOperationException(
                    $"NodeInstance '{i.id}' references missing node '{i.nodeId}'."
                );
        }

        // ---- Modifiers -> Nodes (if scoped)
        if (gd.modifiers != null)
        {
            foreach (var m in gd.modifiers)
            {
                if (!string.IsNullOrEmpty(m.scope?.nodeId) &&
                    !nodeIds.Contains(m.scope.nodeId))
                {
                    throw new InvalidOperationException(
                        $"Modifier '{m.id}' references missing node '{m.scope.nodeId}'."
                    );
                }
            }
        }

        // ---- Upgrades basic integrity
        if (gd.upgrades != null)
        {
            var upgradeIds = new HashSet<string>();
            foreach (var u in gd.upgrades)
            {
                if (string.IsNullOrWhiteSpace(u.id))
                    throw new InvalidOperationException("Upgrade with empty id.");
                if (!upgradeIds.Add(u.id))
                    throw new InvalidOperationException($"Duplicate upgrade id: {u.id}");
            }
        }
    }
}