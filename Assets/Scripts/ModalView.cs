using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(GraphicRaycaster))]
public class ModalView : MonoBehaviour
{
    [SerializeField] private bool dismissible = true;
    [SerializeField] private bool closeOnBackdrop = true;
    [SerializeField] private bool closeOnEscape = true;

    [SerializeField] private Canvas canvas;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private GraphicRaycaster graphicRaycaster;

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

    public void RequestClose()
    {
        if (!Dismissible)
            return;

        if (Manager == null)
        {
            Debug.LogError("ModalView: Manager is not set; cannot close modal. Ensure this modal was shown via ModalManager.", this);
            return;
        }

        Manager.CloseTop();
    }

    public void ShowById(string id)
    {
        if (Manager == null)
        {
            Debug.LogError("ModalView: Manager is not set; cannot ShowById.", this);
            return;
        }
        Manager.ShowById(id);
    }

    protected IUpgradesContext UpgradesContext
    {
        get
        {
            if (Manager == null)
            {
                Debug.LogError("ModalView: Manager is not set; cannot resolve UpgradesContext.", this);
                return null;
            }

            if (Manager is IUpgradesContext ctx)
                return ctx;

            Debug.LogError("ModalView: ModalManager does not provide IUpgradesContext. Wire a UiServiceRegistry into ModalManager (or have ModalManager implement IUpgradesContext) so modals don't need scene lookups.", this);
            return null;
        }
    }

    private void EnsureModalSetup()
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