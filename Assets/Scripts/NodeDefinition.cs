using System;
using System.Collections.Generic;

[Serializable]
public sealed class NodeDefinition
{
    // Identity
    public string id;
    public string type;
    public string displayName;
    public string zoneId;
    public string[] tags;

    // Production
    public CycleDefinition cycle;

    // Leveling / economy
    public LevelingDefinition leveling;

    // Automation defaults
    public AutomationDefinition automation;

    // Outputs (merged from NodeOutputs during import)
    public List<NodeOutputDefinition> outputs;
}

[Serializable]
public sealed class CycleDefinition
{
    public double baseDurationSeconds;
}

[Serializable]
public sealed class LevelingDefinition
{
    public string levelResource;
    public int baseLevel;
    public int maxLevel;

    public PriceCurveDefinition priceCurve;
}

[Serializable]
public sealed class PriceCurveDefinition
{
    public string type;
    public double basePrice;
    public double growth;
    public double increment;
}

[Serializable]
public sealed class AutomationDefinition
{
    public string policy;
    public bool autoCollect;
    public bool autoRestart;
}

[Serializable]
public sealed class NodeOutputDefinition
{
    public string resource;

    // Only one of these is typically used depending on mode
    public string mode; // perCycle / perSecond / payout
    public double basePerSecond;
    public double basePayout;
    public double amountPerCycle;
}
