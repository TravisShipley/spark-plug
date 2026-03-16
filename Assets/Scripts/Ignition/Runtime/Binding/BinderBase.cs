using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Ignition.Binding
{
    public abstract class BinderBase : MonoBehaviour, IBinder
    {
        [FormerlySerializedAs("sourceProvider")]
        [SerializeField]
        private DataProvider dataProvider;

        [SerializeField]
        private string selectedMemberName;

        public DataProvider DataProvider => dataProvider;
        public string SelectedMemberName => selectedMemberName;
        public abstract Type BindingValueType { get; }

        public abstract void Rebind();

        protected bool TryResolveBindingSource(
            out BindingMemberMetadata metadata,
            out object bindingTarget,
            out string logMessage,
            out bool isWarning
        )
        {
            metadata = null;
            bindingTarget = null;
            logMessage = null;
            isWarning = false;

            if (DataProvider == null)
            {
                logMessage = $"{GetType().Name}: data provider is not assigned.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(SelectedMemberName))
            {
                logMessage = $"{GetType().Name}: no bindable member is selected.";
                isWarning = true;
                return false;
            }

            if (
                !BindingMetadataUtility.TryParseBindingKey(
                    SelectedMemberName,
                    out var scope,
                    out var memberName
                )
            )
            {
                logMessage =
                    $"{GetType().Name}: binding key '{SelectedMemberName}' is invalid. Only root members and 'Data.<MemberName>' are supported.";
                return false;
            }

            object sourceObject;
            switch (scope)
            {
                case BindingMemberScope.Provider:
                    sourceObject = DataProvider;
                    break;

                case BindingMemberScope.Data:
                    if (DataProvider is not IBindingDataProvider provider)
                    {
                        logMessage =
                            $"{GetType().Name}: data provider must implement {nameof(IBindingDataProvider)}.";
                        return false;
                    }

                    sourceObject = provider.GetBindingData();
                    if (sourceObject == null)
                    {
                        logMessage =
                            $"{GetType().Name}: binding data is null during rebind for '{SelectedMemberName}'.";
                        isWarning = true;
                        return false;
                    }
                    break;

                default:
                    logMessage =
                        $"{GetType().Name}: binding scope '{scope}' is not supported.";
                    return false;
            }

            if (
                !BindingMetadataUtility.TryGetBindableMember(
                    sourceObject.GetType(),
                    scope,
                    memberName,
                    out metadata,
                    out var validationMessage
                )
            )
            {
                logMessage = $"{GetType().Name}: {validationMessage}";
                return false;
            }

            bindingTarget = sourceObject;
            return true;
        }

        public virtual string GetEditorWarning()
        {
            return null;
        }
    }
}
