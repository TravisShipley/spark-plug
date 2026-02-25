using System;

[Serializable]
public sealed class BuyModeDefinition
{
    public string id;
    public string displayName;
    public string kind;
    public int fixedCount;
    public string[] tags;
}
