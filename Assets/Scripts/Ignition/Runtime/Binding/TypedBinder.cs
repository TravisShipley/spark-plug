using System;
using System.Reflection;
using UniRx;
using UnityEngine;

namespace Ignition.Binding
{
    public abstract class TypedBinder<T> : BinderBase
    {
        private const BindingFlags PropertyFlags = BindingFlags.Instance | BindingFlags.Public;

        private IDisposable subscription;

        public override Type BindingValueType => typeof(T);

        public override void Rebind()
        {
            DisposeSubscription();

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
                    $"{GetType().Name}: data provider must implement {nameof(IBindingDataProvider)}.",
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
                    $"{GetType().Name}: bindable member '{metadata.MemberName}' resolved to null.",
                    this
                );
                return;
            }

            if (propertyValue is IReadOnlyReactiveProperty<T> readOnlyReactiveProperty)
            {
                subscription = readOnlyReactiveProperty.Subscribe(ApplyValue, HandleBindingError);
                return;
            }

            if (propertyValue is IObservable<T> observable)
            {
                subscription = observable.Subscribe(ApplyValue, HandleBindingError);
                return;
            }

            Debug.LogError(
                $"{GetType().Name}: '{metadata.MemberName}' must expose {typeof(T).Name} via IReadOnlyReactiveProperty<T> or IObservable<T>.",
                this
            );
        }

        public override string GetEditorWarning()
        {
            return GetTargetWarning();
        }

        protected abstract void ApplyValue(T value);

        protected virtual string GetTargetWarning()
        {
            return null;
        }

        protected virtual void OnDestroy()
        {
            DisposeSubscription();
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

            if (!BindingMetadataUtility.IsBindable(property))
            {
                Debug.LogError(
                    $"{GetType().Name}: '{sourceType.Name}.{SelectedMemberName}' is not marked with [Bindable].",
                    this
                );
                return false;
            }

            if (!BindingMetadataUtility.TryCreateMetadata(property, out metadata))
            {
                Debug.LogError(
                    $"{GetType().Name}: '{sourceType.Name}.{SelectedMemberName}' must return IReadOnlyReactiveProperty<T>, ReactiveProperty<T>, or IObservable<T>.",
                    this
                );
                return false;
            }

            if (!BindingMetadataUtility.IsExactValueTypeMatch(metadata, typeof(T)))
            {
                Debug.LogError(
                    $"{GetType().Name}: '{sourceType.Name}.{SelectedMemberName}' emits '{metadata.ValueType.Name}', but this binder requires '{typeof(T).Name}'.",
                    this
                );
                return false;
            }

            return true;
        }

        private void HandleBindingError(Exception exception)
        {
            Debug.LogError($"{GetType().Name}: binding stream failed. {exception.Message}", this);
        }

        private void DisposeSubscription()
        {
            subscription?.Dispose();
            subscription = null;
        }
    }
}
