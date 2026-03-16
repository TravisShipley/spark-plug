using System;
using System.Reflection;
using UniRx;
using UnityEngine;

namespace Ignition.Binding
{
    public abstract class TypedBinder<T> : BinderBase
    {
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
                    $"{GetType().Name}: bindable member '{metadata.SerializedKey}' resolved to null.",
                    this
                );
                return;
            }

            if (!BindingMetadataUtility.IsExactValueTypeMatch(metadata, typeof(T)))
            {
                Debug.LogError(
                    $"{GetType().Name}: '{metadata.SerializedKey}' emits '{metadata.ValueType.Name}', but this binder requires '{typeof(T).Name}'.",
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
                $"{GetType().Name}: '{metadata.SerializedKey}' must expose {typeof(T).Name} via IReadOnlyReactiveProperty<T> or IObservable<T>.",
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
