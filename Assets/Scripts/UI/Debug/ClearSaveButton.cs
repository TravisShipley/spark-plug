using UnityEngine;
using UniRx;

public class ClearSaveButton : MonoBehaviour
{
    [SerializeField] private ReactiveButtonView buttonView;
    private bool isBound;

    public void Bind(GameEventStream gameEventStream)
    {
        if (isBound)
        {
            Debug.LogWarning("ClearSaveButton: Bind called more than once. Ignoring.", this);
            return;
        }

        if (gameEventStream == null)
            throw new System.ArgumentNullException(nameof(gameEventStream));

        if (buttonView == null)
        {
            Debug.LogError("ClearSaveButton: ReactiveButtonView is not assigned.", this);
            return;
        }

        buttonView.Bind(
            labelText: Observable.Return("Reset"),
            interactable: Observable.Return(true),
            visible: Observable.Return(true),
            onClick: gameEventStream.RequestResetSave
        );

        isBound = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (buttonView == null)
            buttonView = GetComponent<ReactiveButtonView>();
    }
#endif
}
