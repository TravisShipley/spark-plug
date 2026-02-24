using System;

[Serializable]
public sealed class PrestigeResetScopes
{
    public bool resetNodes;
    public bool resetSoftCurrencies;
    public bool keepHardCurrencies;
    public string[] keepUnlocks;
    public string[] keepUpgrades;
    public string[] keepProjects;
}
