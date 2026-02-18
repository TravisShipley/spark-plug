using UnityEngine;
using UniRx;

public class ClearSaveButton : MonoBehaviour
{
    [SerializeField] private ReactiveButtonView buttonView;

    private void Awake()
    {
        if (buttonView == null)
        {
            Debug.LogError("ClearSaveButton: ReactiveButtonView is not assigned.", this);
            return;
        }

        buttonView.Bind(
            labelText: Observable.Return("Reset"),
            interactable: Observable.Return(true),
            visible: Observable.Return(true),
            onClick: () => EventSystem.OnResetSaveRequested.OnNext(Unit.Default)
        );
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (buttonView == null)
            buttonView = GetComponent<ReactiveButtonView>();
    }
#endif
}
