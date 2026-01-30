using UniRx;
using UnityEngine;

public sealed class BottomBarView : MonoBehaviour
{
    [SerializeField] private ReactiveButtonView upgradesButton;
    [SerializeField] private ReactiveButtonView managersButton;
    [SerializeField] private ReactiveButtonView storeButton;

    private readonly CompositeDisposable disposables = new();
    private bool isBound;

    public void Bind(BottomBarViewModel vm)
    {
        if (isBound)
        {
            Debug.LogWarning("BottomBarView: Bind called more than once; ignoring to avoid duplicate button subscriptions.", this);
            return;
        }
        isBound = true;
        disposables.Clear();

        upgradesButton.Bind(
            labelText: Observable.Return("Upgrades"),
            interactable: vm.ShowUpgrades.CanExecute,
            visible: vm.ShowUpgrades.IsVisible,
            onClick: vm.ShowUpgrades.Execute
        );

        managersButton.Bind(
            labelText: Observable.Return("Managers"),
            interactable: Observable.Return(false),
            visible: vm.ShowManagers.IsVisible,
            onClick: vm.ShowManagers.Execute
        );

        storeButton.Bind(
            labelText: Observable.Return("Store"),
            interactable: Observable.Return(false),
            visible: vm.ShowStore.IsVisible,
            onClick: vm.ShowStore.Execute
        );

    }

    private void OnDestroy()
    {
        disposables.Dispose();
    }
}