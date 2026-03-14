using System;
using System.Reflection;
using UniRx;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public sealed class ButtonCommandBinder : MonoBehaviour, IBinder
{
    private const BindingFlags PropertyFlags = BindingFlags.Instance | BindingFlags.Public;

    [SerializeField]
    private DataProvider dataProvider;

    [SerializeField]
    private string selectedMemberName;

    [SerializeField]
    private Button target;

    [SerializeField]
    private bool bindCanExecuteToInteractable = true;

    private IDisposable canExecuteSubscription;
    private ICommand command;

    public void Rebind()
    {
        DisposeBinding();

        var targetWarning = GetTargetWarning();
        if (!string.IsNullOrWhiteSpace(targetWarning))
        {
            Debug.LogWarning(targetWarning, this);
            return;
        }

        if (dataProvider == null)
        {
            Debug.LogError($"{nameof(ButtonCommandBinder)}: data provider is not assigned.", this);
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedMemberName))
        {
            Debug.LogWarning(
                $"{nameof(ButtonCommandBinder)}: no bindable command member is selected.",
                this
            );
            return;
        }

        object bindingData;
        try
        {
            bindingData = dataProvider.GetBindingData();
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"{nameof(ButtonCommandBinder)}: failed to get binding data. {exception.Message}",
                this
            );
            return;
        }

        if (bindingData == null)
        {
            Debug.LogWarning(
                $"{nameof(ButtonCommandBinder)}: binding data is null during rebind.",
                this
            );
            return;
        }

        if (!TryResolveCommandProperty(bindingData.GetType(), out var property))
            return;

        object propertyValue;
        try
        {
            propertyValue = property.GetValue(bindingData);
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogError(
                $"{nameof(ButtonCommandBinder)}: failed to read '{property.Name}' from '{bindingData.GetType().Name}'. {exception.InnerException?.Message ?? exception.Message}",
                this
            );
            return;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"{nameof(ButtonCommandBinder)}: failed to read '{property.Name}' from '{bindingData.GetType().Name}'. {exception.Message}",
                this
            );
            return;
        }

        if (propertyValue is not ICommand resolvedCommand)
        {
            Debug.LogError(
                $"{nameof(ButtonCommandBinder)}: '{bindingData.GetType().Name}.{property.Name}' must return {nameof(ICommand)}.",
                this
            );
            return;
        }

        command = resolvedCommand;
        target.onClick.AddListener(ExecuteCommand);

        if (!bindCanExecuteToInteractable)
            return;

        if (command.CanExecute == null)
        {
            Debug.LogWarning(
                $"{nameof(ButtonCommandBinder)}: '{bindingData.GetType().Name}.{property.Name}' returned a command with no CanExecute stream.",
                this
            );
            return;
        }

        canExecuteSubscription = command.CanExecute.Subscribe(
            value => target.interactable = value,
            exception =>
                Debug.LogError(
                    $"{nameof(ButtonCommandBinder)}: CanExecute stream failed. {exception.Message}",
                    this
                )
        );
    }

    private void OnDestroy()
    {
        DisposeBinding();
    }

    private void ExecuteCommand()
    {
        command?.Execute();
    }

    private void DisposeBinding()
    {
        canExecuteSubscription?.Dispose();
        canExecuteSubscription = null;

        if (target != null)
            target.onClick.RemoveListener(ExecuteCommand);

        command = null;
    }

    private bool TryResolveCommandProperty(Type sourceType, out PropertyInfo property)
    {
        property = sourceType.GetProperty(selectedMemberName, PropertyFlags);
        if (property == null)
        {
            Debug.LogError(
                $"{nameof(ButtonCommandBinder)}: could not find public property '{selectedMemberName}' on '{sourceType.Name}'.",
                this
            );
            return false;
        }

        if (!Attribute.IsDefined(property, typeof(BindableCommandAttribute), true))
        {
            Debug.LogError(
                $"{nameof(ButtonCommandBinder)}: '{sourceType.Name}.{selectedMemberName}' is not marked with [{nameof(BindableCommandAttribute).Replace(nameof(Attribute), string.Empty)}].",
                this
            );
            return false;
        }

        if (!typeof(ICommand).IsAssignableFrom(property.PropertyType))
        {
            Debug.LogError(
                $"{nameof(ButtonCommandBinder)}: '{sourceType.Name}.{selectedMemberName}' must return {nameof(ICommand)}.",
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
