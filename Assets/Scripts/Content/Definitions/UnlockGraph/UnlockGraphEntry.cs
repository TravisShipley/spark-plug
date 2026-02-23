using System;

[Serializable]
public sealed class UnlockGraphEntry
{
    public string id;
    public string zoneId;
    public string targetNodeInstanceId;
    public UnlockRequirement[] requirements;

    public UnlockTarget[] unlocks;
}
