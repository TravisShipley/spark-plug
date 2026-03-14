using UnityEngine;
using UnityEngine.UI;

public sealed class FillBinder : TypedBinder<float>
{
    [SerializeField]
    private Image target;

    protected override void ApplyValue(float value)
    {
        if (target == null)
            return;

        target.fillAmount = Mathf.Clamp01(value);
    }

    protected override string GetTargetWarning()
    {
        return target == null ? $"{nameof(FillBinder)}: target Image is not assigned." : null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (target == null)
            target = GetComponent<Image>();
    }
#endif
}
