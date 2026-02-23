using System;

[Serializable]
public sealed class UnlockRequirement
{
    public string type;
    public string nodeInstanceId;
    public int minLevel;
    public string upgradeId;
    public UnlockRequirementArgs args;
}
