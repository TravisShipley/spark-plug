using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(GraphicRaycaster))]
[DisallowMultipleComponent]
public abstract class UiScreenView : MonoBehaviour
{
    [SerializeField]
    private bool dismissible = true;

    [SerializeField]
    private bool closeOnBackdrop = true;

    [SerializeField]
    private bool closeOnEscape = true;

    [SerializeField]
    private Canvas canvas;

    [SerializeField]
    private CanvasGroup canvasGroup;

    [SerializeField]
    private GraphicRaycaster graphicRaycaster;

    public bool Dismissible => dismissible;
    public bool CloseOnBackdrop => closeOnBackdrop;
    public bool CloseOnEscape => closeOnEscape;

    public Canvas Canvas => canvas;
    public CanvasGroup CanvasGroup => canvasGroup;
    public UiScreenManager Manager { get; internal set; }

    private void Awake()
    {
        EnsureScreenSetup();
    }

    public void RequestClose()
    {
        if (!Dismissible)
            return;

        if (Manager == null)
        {
            Debug.LogError(
                "UiScreenView: Manager is not set; cannot close screen. Ensure this screen was shown via UiScreenManager.",
                this
            );
            return;
        }

        Manager.CloseTop();
    }

    public void ShowById(string id)
    {
        if (Manager == null)
        {
            Debug.LogError("UiScreenView: Manager is not set; cannot ShowById.", this);
            return;
        }
        Manager.ShowById(id);
    }

    private void EnsureScreenSetup()
    {
        if (canvas == null)
            canvas = GetComponent<Canvas>();

        if (canvas != null)
            canvas.overrideSorting = true;

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (graphicRaycaster == null)
            graphicRaycaster = GetComponent<GraphicRaycaster>();
    }

    public virtual void OnBeforeShow(object payload) { }

    public virtual void OnShown() { }

    public virtual void OnBeforeClose() { }
}
