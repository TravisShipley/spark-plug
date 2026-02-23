using System;

[Serializable]
public sealed class MetaUpgradeDefinition
{
    public string id;
    public ComputedFormula computed;
    public string writesToState;
}
