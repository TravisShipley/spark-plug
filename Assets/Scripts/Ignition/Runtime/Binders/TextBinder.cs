using TMPro;
using UnityEngine;

public sealed class TextBinder : TypedBinder<string>
{
    [SerializeField]
    private TMP_Text target;

    protected override void ApplyValue(string value)
    {
        if (target == null)
            return;

        target.text = value ?? string.Empty;
    }

    protected override string GetTargetWarning()
    {
        return target == null ? $"{nameof(TextBinder)}: target TMP_Text is not assigned." : null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (target == null)
            target = GetComponent<TMP_Text>();
    }
#endif
}
