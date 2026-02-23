using System;

[Serializable]
public sealed class ModifierEntry
{
    public string id;
    public string source;
    public string zoneId;
    public ModifierScope scope;
    public string operation;
    public string target;
    public double value;
}
