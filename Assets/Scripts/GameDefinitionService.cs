using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class GameDefinitionService
{
    public const string DefaultPath = "Assets/Data/game_definition.json";
    private readonly string path;
    private GameDefinition definition;
    private UpgradeCatalog catalog;

    public GameDefinitionService(string projectRelativePath = "Assets/Data/game_definition.json")
    {
        path = projectRelativePath;
        Reload();
    }

    public void Reload()
    {
        definition = GameDefinitionLoader.LoadFromFile(path);
        catalog = new UpgradeCatalog(definition?.upgrades);
    }

    public IReadOnlyList<UpgradeEntry> Upgrades => catalog?.Upgrades ?? new List<UpgradeEntry>();

    public bool TryGetUpgrade(string id, out UpgradeEntry entry)
    {
        if (catalog == null)
        {
            entry = null;
            return false;
        }

        return catalog.TryGet(id, out entry);
    }

    public UpgradeCatalog Catalog => catalog;
}
