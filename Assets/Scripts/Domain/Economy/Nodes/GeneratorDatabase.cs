using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Idle/Generator Database", fileName = "GeneratorDatabase")]
public class GeneratorDatabase : ScriptableObject
{
    [SerializeField] private List<GeneratorDefinition> generators = new();

    // Runtime lookup (rebuilt on enable / validate)
    private Dictionary<string, GeneratorDefinition> byId;

    public IReadOnlyList<GeneratorDefinition> Generators => generators;

    public bool TryGet(string id, out GeneratorDefinition def)
    {
        EnsureIndex();
        return byId.TryGetValue(id, out def);
    }

    public GeneratorDefinition GetRequired(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new System.ArgumentException("Generator id is null/empty.", nameof(id));

        EnsureIndex();

        if (!byId.TryGetValue(id, out var def) || def == null)
            throw new KeyNotFoundException($"GeneratorDatabase: No GeneratorDefinition found for id '{id}'.");

        return def;
    }

    private void OnEnable() => RebuildIndex();

#if UNITY_EDITOR
    private void OnValidate() => RebuildIndex();
#endif

    private void EnsureIndex()
    {
        if (byId == null || byId.Count != generators.Count)
            RebuildIndex();
    }

    private void RebuildIndex()
    {
        byId = new Dictionary<string, GeneratorDefinition>(generators.Count);

        foreach (var def in generators)
        {
            if (def == null)
                continue;

            if (string.IsNullOrWhiteSpace(def.Id))
            {
                Debug.LogError($"GeneratorDatabase: A GeneratorDefinition is missing Id. Asset: {def.name}", this);
                continue;
            }

            if (byId.ContainsKey(def.Id))
            {
                Debug.LogError($"GeneratorDatabase: Duplicate generator Id '{def.Id}'. Asset: {def.name}", this);
                continue;
            }

            byId.Add(def.Id, def);
        }
    }
}