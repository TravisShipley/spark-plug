using System;

[Serializable]
public sealed class NodeInstanceDefinition
{
    public string id;
    public string nodeId;
    public string zoneId;

    public string displayNameOverride;
    public string[] tags;

    public NodeInstanceInitialStateDefinition initialState;
}

[Serializable]
public sealed class NodeInstanceInitialStateDefinition
{
    public int level;
    public bool enabled;
}
