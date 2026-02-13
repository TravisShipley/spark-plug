public enum UpgradeEffectType
{
    NodeSpeedMultiplier,
    ResourceGain,
    NodeOutput,
    NodeInput,
    NodeCapacityThroughputPerSecond,
    StateValue,
    VariableValue,
    AutomationPolicy,

    // Legacy aliases kept for backward compatibility with existing runtime/UI logic.
    OutputMultiplier = NodeOutput,
    SpeedMultiplier = NodeSpeedMultiplier
}
