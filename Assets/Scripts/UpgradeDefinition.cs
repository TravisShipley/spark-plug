using UnityEngine;

[CreateAssetMenu(menuName = "Idle/Upgrade Definition", fileName = "UpgradeDefinition")]
public class UpgradeDefinition : ScriptableObject
{
    [Header("Identity")]
    public string Id;
    public string DisplayName;

    [Header("Target")]
    // If set, this upgrade only applies to a specific generator id.
    // Leave empty to treat as global (future use).
    public string GeneratorId;

    [Header("Economy")]
    public double Cost = 10.0;

    [Header("Effect")]
    public UpgradeEffectType EffectType;

    // Multiplicative factor applied once.
    // Meaning depends on EffectType (eg: 2.0 = x2 output, 0.8 = 20% faster cycle if speed).
    public double Value = 1.0;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Cost < 0)
            Cost = 0;

        // All v1 upgrades are multiplicative and must be >= 1
        if (Value < 1.0)
            Value = 1.0;
    }
#endif
}