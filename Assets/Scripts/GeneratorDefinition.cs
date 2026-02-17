using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Idle/Generator Definition", fileName = "GeneratorDefinition")]
public class GeneratorDefinition : ScriptableObject
{
    public string Id;
    public string DisplayName;

    [Header("Production")]
    public double BaseOutputPerCycle = 1.0;
    public double BaseCycleDurationSeconds = 2.0;
    public string OutputResourceId = "currencySoft";

    [Header("Costs")]
    public double BaseLevelCost = 10.0;
    public string LevelCostResourceId = "currencySoft";
    public double AutomationCost = 123.0;
    public string AutomationCostResourceId = "currencySoft";

    [Header("Growth Values")]
    public double LevelCostGrowth = 1.15;

    [Header("Derived Milestones")]
    public int[] MilestoneLevels = Array.Empty<int>();
}
