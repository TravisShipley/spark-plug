using System;
using UniRx;

public sealed class UiCommand : ICommand
{
    private readonly Action execute;
    private static readonly IReadOnlyReactiveProperty<bool> DefaultCanExecute =
        Observable.Return(true).ToReadOnlyReactiveProperty();

    public IReadOnlyReactiveProperty<bool> CanExecute { get; }

    public UiCommand(Action execute, IReadOnlyReactiveProperty<bool> canExecute = null)
    {
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        CanExecute = canExecute ?? DefaultCanExecute;
    }

    public void Execute()
    {
        if (!CanExecute.Value)
            return;

        execute.Invoke();
    }
}
