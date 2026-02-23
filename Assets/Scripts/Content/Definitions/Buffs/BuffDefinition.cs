using System;

[Serializable]
public sealed class BuffDefinition
{
    public string id;
    public string displayName;
    public string zoneId;
    public int durationSeconds;
    public string stacking;
    public EffectItem[] effects;

    public string effects_json;
    public string[] tags;
}
