using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public sealed class ButtonActionBinder : MonoBehaviour
{
    [SerializeField] private Button button;

    [Header("Action")]
    [SerializeField] private UnityEvent onClicked;

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        button.onClick.AddListener(InvokeAction);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(InvokeAction);
    }

    public void InvokeAction()
    {
        onClicked?.Invoke();
    }
}