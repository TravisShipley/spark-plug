using UniRx;

public interface ICommand
{
    IReadOnlyReactiveProperty<bool> CanExecute { get; }
    void Execute();
}
