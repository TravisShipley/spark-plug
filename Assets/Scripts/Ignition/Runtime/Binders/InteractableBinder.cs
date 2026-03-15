using Ignition.Binding;
using UnityEngine;
using UnityEngine.UI;

namespace Ignition.Binders
{
    public sealed class InteractableBinder : TypedBinder<bool>
    {
        [SerializeField]
        private Selectable target;

        protected override void ApplyValue(bool value)
        {
            if (target == null)
                return;

            target.interactable = value;
        }

        protected override string GetTargetWarning()
        {
            return target == null
                ? $"{nameof(InteractableBinder)}: target Selectable is not assigned."
                : null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (target == null)
                target = GetComponent<Selectable>();
        }
#endif
    }
}
