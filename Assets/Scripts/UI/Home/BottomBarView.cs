using UniRx;
using UnityEngine;

public sealed class BottomBarView : MonoBehaviour
{
    [SerializeField]
    private ReactiveButtonView upgradesButton;

    [SerializeField]
    private ReactiveButtonView managersButton;

    [SerializeField]
    private ReactiveButtonView prestigeButton;

    [Header("Badges")]
    [SerializeField]
    private GameObject upgradesBadge;

    [SerializeField]
    private GameObject managersBadge;

    private readonly CompositeDisposable disposables = new();
    private bool isBound;

    public void Bind(BottomBarViewModel vm)
    {
        if (isBound)
        {
            Debug.LogWarning(
                "BottomBarView: Bind called more than once; ignoring to avoid duplicate button subscriptions.",
                this
            );
            return;
        }

        if (vm == null)
        {
            Debug.LogError("BottomBarView: viewModel is null.", this);
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
            interactable: vm.ShowManagers.CanExecute,
            visible: vm.ShowManagers.IsVisible,
            onClick: vm.ShowManagers.Execute
        );

        prestigeButton.Bind(
            labelText: Observable.Return("Prestige"),
            interactable: vm.ShowPrestige.CanExecute,
            visible: vm.ShowPrestige.IsVisible,
            onClick: vm.ShowPrestige.Execute
        );

        if (upgradesBadge != null)
        {
            vm.ShowUpgradesBadge.DistinctUntilChanged()
                .Subscribe(show => upgradesBadge.SetActive(show))
                .AddTo(disposables);
        }

        if (managersBadge != null)
        {
            vm.ShowManagersBadge.DistinctUntilChanged()
                .Subscribe(show => managersBadge.SetActive(show))
                .AddTo(disposables);
        }
    }

    private void OnDestroy()
    {
        disposables.Dispose();
    }
}
