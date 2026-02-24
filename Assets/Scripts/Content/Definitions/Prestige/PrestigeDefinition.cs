using System;

[Serializable]
public sealed class PrestigeDefinition
{
    public bool enabled;
    public string zoneId;
    public string prestigeResource;
    public PrestigeFormula formula;
    public PrestigeResetScopes resetScopes;
    public MetaUpgradeDefinition[] metaUpgrades;
}
