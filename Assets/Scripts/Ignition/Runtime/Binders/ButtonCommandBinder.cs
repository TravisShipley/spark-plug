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
        private const BindingFlags PropertyFlags = BindingFlags.Instance | BindingFlags.Public;

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

            if (DataProvider == null)
            {
                Debug.LogError($"{GetType().Name}: data provider is not assigned.", this);
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedMemberName))
            {
                Debug.LogWarning($"{GetType().Name}: no bindable member is selected.", this);
                return;
            }

            if (DataProvider is not IBindingDataProvider provider)
            {
                Debug.LogError(
                    $"{GetType().Name}: assigned data provider does not implement {nameof(IBindingDataProvider)}.",
                    this
                );
                return;
            }

            var bindingData = provider.GetBindingData();
            if (bindingData == null)
            {
                Debug.LogWarning($"{GetType().Name}: binding data is null during rebind.", this);
                return;
            }

            if (!TryResolveMetadata(bindingData.GetType(), out var metadata))
                return;

            object propertyValue;
            try
            {
                propertyValue = metadata.Property.GetValue(bindingData);
            }
            catch (TargetInvocationException exception)
            {
                Debug.LogError(
                    $"{GetType().Name}: failed to read '{metadata.MemberName}' from '{bindingData.GetType().Name}'. {exception.InnerException?.Message ?? exception.Message}",
                    this
                );
                return;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"{GetType().Name}: failed to read '{metadata.MemberName}' from '{bindingData.GetType().Name}'. {exception.Message}",
                    this
                );
                return;
            }

            if (propertyValue == null)
            {
                Debug.LogWarning(
                    $"{GetType().Name}: '{bindingData.GetType().Name}.{metadata.MemberName}' returned null.",
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
                $"{GetType().Name}: '{metadata.MemberName}' must expose {nameof(ICommand)}.",
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

        private bool TryResolveMetadata(Type sourceType, out BindingMemberMetadata metadata)
        {
            metadata = null;

            var property = sourceType.GetProperty(SelectedMemberName, PropertyFlags);
            if (property == null)
            {
                Debug.LogError(
                    $"{GetType().Name}: could not find public property '{SelectedMemberName}' on '{sourceType.Name}'.",
                    this
                );
                return false;
            }

            if (
                !BindingMetadataUtility.IsBindable(property)
                && !BindingMetadataUtility.IsBindableCommand(property)
            )
            {
                Debug.LogError(
                    $"{GetType().Name}: '{sourceType.Name}.{SelectedMemberName}' is not marked with [Bindable] or [BindableCommand].",
                    this
                );
                return false;
            }

            if (!BindingMetadataUtility.TryCreateMetadata(property, out metadata))
            {
                Debug.LogError(
                    $"{GetType().Name}: '{sourceType.Name}.{SelectedMemberName}' must return {nameof(ICommand)}.",
                    this
                );
                return false;
            }

            if (!BindingMetadataUtility.IsExactValueTypeMatch(metadata, typeof(ICommand)))
            {
                Debug.LogError(
                    $"{GetType().Name}: '{sourceType.Name}.{SelectedMemberName}' emits '{metadata.ValueType.Name}', but this binder requires '{nameof(ICommand)}'.",
                    this
                );
                return false;
            }

            return true;
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
