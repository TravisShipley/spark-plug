using System;
using System.Collections.Generic;

public sealed class ResourceCatalog
{
    private readonly List<ResourceDefinition> resources;
    private readonly Dictionary<string, ResourceDefinition> byId;

    public ResourceCatalog(IReadOnlyList<ResourceDefinition> definitions)
    {
        resources = new List<ResourceDefinition>();
        byId = new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal);

        if (definitions == null)
            return;

        for (int i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            if (definition == null)
                continue;

            var id = (definition.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
                continue;

            if (byId.ContainsKey(id))
                continue;

            resources.Add(definition);
            byId[id] = definition;
        }
    }

    public IReadOnlyList<ResourceDefinition> Resources => resources;

    public bool TryGet(string id, out ResourceDefinition definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(id))
            return false;

        return byId.TryGetValue(id.Trim(), out definition) && definition != null;
    }

    public ResourceDefinition GetRequired(string id)
    {
        if (TryGet(id, out var definition))
            return definition;

        throw new KeyNotFoundException($"ResourceCatalog: Unknown resource id '{id}'.");
    }
}
