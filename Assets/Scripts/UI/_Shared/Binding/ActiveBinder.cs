using UnityEngine;

public sealed class ActiveBinder : TypedBinder<bool>
{
    [SerializeField]
    private GameObject target;

    protected override void ApplyValue(bool value)
    {
        if (target == null)
            return;

        target.SetActive(value);
    }

    protected override string GetTargetWarning()
    {
        return target == null ? $"{nameof(ActiveBinder)}: target GameObject is not assigned." : null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (target == null)
            target = gameObject;
    }
#endif
}
