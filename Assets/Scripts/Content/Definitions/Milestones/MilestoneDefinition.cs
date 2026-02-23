using System;

[Serializable]
public sealed class MilestoneDefinition
{
    public string id;
    public string nodeId;
    public string zoneId;
    public int atLevel;
    public EffectItem[] grantEffects;
}
