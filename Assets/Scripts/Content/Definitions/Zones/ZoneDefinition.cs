using System;

[Serializable]
public sealed class ZoneDefinition
{
    public string id;
    public string displayName;
    public string description;
    public string startingPhaseId;
    public string[] localResources;
    public string[] tags;
}
