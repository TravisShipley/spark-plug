public sealed class UiCommand
{
    private readonly Action execute;
    public UiCommand(Action execute) => this.execute = execute;
    public void Execute() => execute?.Invoke();
}