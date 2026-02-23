using System;

[Serializable]
public sealed class MilestoneEntry
{
    public string id;
    public string nodeId;
    public string zoneId;
    public int atLevel;
    public EffectItem[] grantEffects;
}
