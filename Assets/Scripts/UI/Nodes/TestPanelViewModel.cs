using System;
using UniRx;

public sealed class TestPanelViewModel : IDisposable
{
    private readonly CompositeDisposable disposables = new();
    private readonly ReactiveProperty<string> name;
    private readonly ReactiveProperty<string> incomeText;
    private readonly ReactiveProperty<float> progress;
    private readonly ReactiveProperty<bool> isVisible;
    private readonly ReactiveProperty<bool> isInteractable;

    public TestPanelViewModel(
        string name,
        string incomeText,
        float progress,
        bool isVisible,
        bool isInteractable
    )
    {
        this.name = new ReactiveProperty<string>(name).AddTo(disposables);
        this.incomeText = new ReactiveProperty<string>(incomeText).AddTo(disposables);
        this.progress = new ReactiveProperty<float>(progress).AddTo(disposables);
        this.isVisible = new ReactiveProperty<bool>(isVisible).AddTo(disposables);
        this.isInteractable = new ReactiveProperty<bool>(isInteractable).AddTo(disposables);
    }

    [Bindable("Display Name")]
    public IReadOnlyReactiveProperty<string> Name => name;

    [Bindable("Income")]
    public IReadOnlyReactiveProperty<string> IncomeText => incomeText;

    [Bindable]
    public IReadOnlyReactiveProperty<float> Progress => progress;

    [Bindable]
    public IReadOnlyReactiveProperty<bool> IsVisible => isVisible;

    [Bindable]
    public IReadOnlyReactiveProperty<bool> IsInteractable => isInteractable;

    public void SetName(string value)
    {
        name.Value = value;
    }

    public void SetIncomeText(string value)
    {
        incomeText.Value = value;
    }

    public void SetProgress(float value)
    {
        progress.Value = value;
    }

    public void SetVisible(bool value)
    {
        isVisible.Value = value;
    }

    public void SetInteractable(bool value)
    {
        isInteractable.Value = value;
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
