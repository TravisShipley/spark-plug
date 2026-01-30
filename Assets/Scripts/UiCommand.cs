using System;
using UniRx;

public sealed class UiCommand
{
    private readonly Action execute;

    public IReadOnlyReactiveProperty<bool> CanExecute { get; }
    public IReadOnlyReactiveProperty<bool> IsVisible { get; }

    public UiCommand(
        Action execute,
        IReadOnlyReactiveProperty<bool> canExecute = null,
        IReadOnlyReactiveProperty<bool> isVisible = null)
    {
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));

        // Default behavior: always executable and visible
        CanExecute = canExecute ?? Observable.Return(true).ToReadOnlyReactiveProperty();
        IsVisible = isVisible ?? Observable.Return(true).ToReadOnlyReactiveProperty();
    }

    public void Execute()
    {
        if (!CanExecute.Value)
            return;

        execute.Invoke();
    }
}