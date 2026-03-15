using UniRx;

namespace Ignition.Commands
{
    public interface ICommand
    {
        IReadOnlyReactiveProperty<bool> CanExecute { get; }
        void Execute();
    }
}
