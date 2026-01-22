using UnityEngine;

[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(CanvasGroup))]
public class ModalView : MonoBehaviour
{
    [SerializeField] private bool dismissible = true;
    [SerializeField] private bool closeOnBackdrop = true;
    [SerializeField] private bool closeOnEscape = true;

    [SerializeField] private Canvas canvas;
    [SerializeField] private CanvasGroup canvasGroup;

    public bool Dismissible => dismissible;
    public bool CloseOnBackdrop => closeOnBackdrop;
    public bool CloseOnEscape => closeOnEscape;

    public Canvas Canvas => canvas;
    public CanvasGroup CanvasGroup => canvasGroup;
    public ModalManager Manager { get; internal set; }

    private void Awake()
    {
        EnsureModalSetup();
    }

    #if UNITY_EDITOR
    private void OnValidate()
    {
        EnsureModalSetup();
    }
    #endif

    public void RequestClose()
    {
        if (!Dismissible)
            return;

        if (Manager != null)
        {
            Manager.CloseTop();
        }
        else
        {
            // Fallback: allow the modal to self-destruct if no manager is present.
            Destroy(gameObject);
        }
    }

    public void ShowById(string id)
    {
        Manager.ShowById(id);
    }

    private void EnsureModalSetup()
    {
        if (canvas == null)
            canvas = GetComponent<Canvas>();

        if (canvas != null)
            canvas.overrideSorting = true;

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
    }

    public virtual void OnBeforeShow(object payload) { }
    public virtual void OnShown() { }
    public virtual void OnBeforeClose() { }
}