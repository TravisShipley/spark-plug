using System.Collections.Generic;

public sealed class GameDefinitionService
{
    public const string DefaultPath = "Assets/Data/game_definition.json";
    private readonly string path;
    private GameDefinition definition;
    private NodeCatalog nodeCatalog;
    private NodeInstanceCatalog nodeInstanceCatalog;
    private UpgradeCatalog upgradeCatalog;

    public GameDefinitionService(string projectRelativePath = "Assets/Data/game_definition.json")
    {
        path = projectRelativePath;
        Reload();
    }

    public void Reload()
    {
        definition = GameDefinitionLoader.LoadFromFile(path);
        nodeCatalog = new NodeCatalog(definition?.nodes);
        nodeInstanceCatalog = new NodeInstanceCatalog(definition?.nodeInstances);
        upgradeCatalog = new UpgradeCatalog(definition?.upgrades);
    }

    public IReadOnlyList<NodeDefinition> Nodes => nodeCatalog?.Nodes ?? new List<NodeDefinition>();
    public IReadOnlyList<NodeInstanceDefinition> NodeInstances =>
        nodeInstanceCatalog?.NodeInstances ?? new List<NodeInstanceDefinition>();
    public IReadOnlyList<UpgradeEntry> Upgrades =>
        upgradeCatalog?.Upgrades ?? new List<UpgradeEntry>();

    public bool TryGetNode(string id, out NodeDefinition node)
    {
        if (nodeCatalog == null)
        {
            node = null;
            return false;
        }

        return nodeCatalog.TryGet(id, out node);
    }

    public bool TryGetNodeInstance(string id, out NodeInstanceDefinition nodeInstance)
    {
        if (nodeInstanceCatalog == null)
        {
            nodeInstance = null;
            return false;
        }

        return nodeInstanceCatalog.TryGet(id, out nodeInstance);
    }

    public bool TryGetUpgrade(string id, out UpgradeEntry entry)
    {
        if (upgradeCatalog == null)
        {
            entry = null;
            return false;
        }

        return upgradeCatalog.TryGet(id, out entry);
    }

    public NodeCatalog NodeCatalog => nodeCatalog;
    public NodeInstanceCatalog NodeInstanceCatalog => nodeInstanceCatalog;
    public UpgradeCatalog UpgradeCatalog => upgradeCatalog;
    public UpgradeCatalog Catalog => upgradeCatalog;
}
