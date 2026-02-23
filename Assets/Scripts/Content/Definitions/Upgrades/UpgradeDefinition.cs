using System;

[Serializable]
public sealed class UpgradeDefinition
{
    public string id;
    public string displayName;

    public Presentation presentation;

    public string category;
    public string zoneId;
    public CostItem[] cost;

    public bool repeatable;
    public int maxRank;
    public RankCostScaling rankCostScaling;

    public EffectItem[] effects;
    public RequirementItem[] requirements;

    public string[] tags;

    public bool enabled = true;
}
