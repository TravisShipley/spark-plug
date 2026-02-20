using UniRx;
using UnityEngine;

public sealed class AdBoostButtonView : MonoBehaviour
{
    [SerializeField]
    private ReactiveButtonView button;

    private bool isBound;

    public void Bind(UiScreenService uiScreenService)
    {
        if (isBound)
        {
            Debug.LogWarning("AdBoostButtonView: Bind called more than once. Ignoring.", this);
            return;
        }

        if (button == null)
        {
            Debug.LogError("AdBoostButtonView: ReactiveButtonView is not assigned.", this);
            return;
        }

        if (uiScreenService == null)
        {
            Debug.LogError("AdBoostButtonView: UiScreenService is null.", this);
            return;
        }

        if (TryGetComponent<ButtonActionBinder>(out var actionBinder) && actionBinder != null)
            actionBinder.enabled = false;

        button.Bind(
            interactable: Observable.Return(true),
            visible: Observable.Return(true),
            onClick: uiScreenService.ShowAdBoost
        );

        isBound = true;
    }
}
