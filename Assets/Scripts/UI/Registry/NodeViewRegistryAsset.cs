using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "NodeViewRegistry",
    menuName = "SparkPlug/UI/Node View Registry"
)]
public sealed class NodeViewRegistryAsset : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string viewId;
        public GameObject prefab;
    }

    [SerializeField]
    private List<Entry> entries = new();

    private Dictionary<string, GameObject> prefabByViewId;

    public bool TryGetPrefab(string viewId, out GameObject prefab)
    {
        prefab = null;

        var id = NormalizeId(viewId);
        if (string.IsNullOrEmpty(id))
            return false;

        EnsureLookup();
        return prefabByViewId.TryGetValue(id, out prefab) && prefab != null;
    }

    private void OnEnable()
    {
        prefabByViewId = null;
    }

    private void OnValidate()
    {
        prefabByViewId = null;
    }

    private void EnsureLookup()
    {
        if (prefabByViewId != null)
            return;

        prefabByViewId = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        if (entries == null)
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var id = NormalizeId(entry?.viewId);
            if (string.IsNullOrEmpty(id) || entry?.prefab == null)
                continue;

            prefabByViewId[id] = entry.prefab;
        }
    }

    private static string NormalizeId(string value)
    {
        return (value ?? string.Empty).Trim();
    }
}
