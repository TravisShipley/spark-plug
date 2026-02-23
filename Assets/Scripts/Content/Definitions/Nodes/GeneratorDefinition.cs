using System;

[Serializable]
public sealed class GeneratorDefinition
{
    public string Id;
    public string DisplayName;

    public double BaseOutputPerCycle = 1.0;
    public double BaseCycleDurationSeconds = 2.0;
    public string OutputResourceId = "currencySoft";

    public double BaseLevelCost = 10.0;
    public string LevelCostResourceId = "currencySoft";
    public double AutomationCost = 123.0;
    public string AutomationCostResourceId = "currencySoft";

    public double LevelCostGrowth = 1.15;

    public int[] MilestoneLevels = Array.Empty<int>();
}
