using UnityEngine;

[CreateAssetMenu(menuName = "Idle/Generator Definition", fileName = "GeneratorDefinition")]
public class GeneratorDefinition : ScriptableObject
{
    public string Id;
    public string DisplayName;

    [Header("Production")]
    public double BaseOutputPerCycle = 1.0;
    public double BaseCycleDurationSeconds = 2.0;

    [Header("Costs")]
    public double BaseLevelCost = 10.0;
    public double AutomationCost = 50.0;

    [Header("Growth Values")]
    public double LevelCostGrowth = 1.15;
}