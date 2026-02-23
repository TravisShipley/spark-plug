using System;

[Serializable]
public sealed class UnlockGraphDefinition
{
    public string id;
    public string zoneId;
    public string targetNodeInstanceId;
    public UnlockRequirement[] requirements;

    public UnlockTarget[] unlocks;
}
