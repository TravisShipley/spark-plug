using System;

[Serializable]
public sealed class ModifierDefinition
{
    public string id;
    public string source;
    public string zoneId;
    public ModifierScope scope;
    public string operation;
    public string target;
    public double value;
}
