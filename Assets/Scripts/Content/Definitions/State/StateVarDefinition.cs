using System;

[Serializable]
public sealed class StateVarDefinition
{
    public string id;
    public string displayName;
    public string kind;
    public double defaultValue;
    public string[] tags;
}
