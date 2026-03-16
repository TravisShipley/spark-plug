using System;
using System.Reflection;
using Ignition.Binding;
using Ignition.Commands;
using UnityEngine;
using UnityEngine.UI;

namespace Ignition.Binders
{
    [RequireComponent(typeof(Button))]
    public sealed class ButtonCommandBinder : BinderBase
    {
        [SerializeField]
        private Button target;

        private ICommand command;

        public override Type BindingValueType => typeof(ICommand);

        public override void Rebind()
        {
            command = null;

            var targetWarning = GetTargetWarning();
            if (!string.IsNullOrWhiteSpace(targetWarning))
            {
                Debug.LogWarning(targetWarning, this);
                return;
            }

            if (
                !TryResolveBindingSource(
                    out var metadata,
                    out var bindingSource,
                    out var logMessage,
                    out var isWarning
                )
            )
            {
                if (isWarning)
                    Debug.LogWarning(logMessage, this);
                else
                    Debug.LogError(logMessage, this);
                return;
            }

            object propertyValue;
            try
            {
                propertyValue = metadata.Property.GetValue(bindingSource);
            }
            catch (TargetInvocationException exception)
            {
                Debug.LogError(
                    $"{GetType().Name}: failed to read '{metadata.SerializedKey}' from '{bindingSource.GetType().Name}'. {exception.InnerException?.Message ?? exception.Message}",
                    this
                );
                return;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"{GetType().Name}: failed to read '{metadata.SerializedKey}' from '{bindingSource.GetType().Name}'. {exception.Message}",
                    this
                );
                return;
            }

            if (propertyValue == null)
            {
                Debug.LogWarning(
                    $"{GetType().Name}: '{metadata.SerializedKey}' returned null.",
                    this
                );
                return;
            }

            if (!BindingMetadataUtility.IsExactValueTypeMatch(metadata, typeof(ICommand)))
            {
                Debug.LogError(
                    $"{GetType().Name}: '{metadata.SerializedKey}' emits '{metadata.ValueType.Name}', but this binder requires '{nameof(ICommand)}'.",
                    this
                );
                return;
            }

            if (propertyValue is ICommand resolvedCommand)
            {
                command = resolvedCommand;
                return;
            }

            Debug.LogError(
                $"{GetType().Name}: '{metadata.SerializedKey}' must expose {nameof(ICommand)}.",
                this
            );
        }

        public override string GetEditorWarning()
        {
            return GetTargetWarning();
        }

        private void Awake()
        {
            if (target == null)
                target = GetComponent<Button>();

            if (target != null)
                target.onClick.AddListener(OnClick);
        }

        private void OnDestroy()
        {
            if (target != null)
                target.onClick.RemoveListener(OnClick);

            command = null;
        }

        private void OnClick()
        {
            command?.Execute();
        }

        private string GetTargetWarning()
        {
            return target == null
                ? $"{nameof(ButtonCommandBinder)}: target Button is not assigned."
                : null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (target == null)
                target = GetComponent<Button>();
        }
#endif
    }
}
