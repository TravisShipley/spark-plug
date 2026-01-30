using System;
using TMPro;
using UniRx;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

[RequireComponent(typeof(Button))]
public class ReactiveButtonView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private GameObject target; // optional; defaults to this GO

    private readonly CompositeDisposable disposables = new();
    private UnityAction clickListener;

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (target == null) target = gameObject;
    }

    public void Bind(
        IObservable<string> labelText = null,
        IObservable<bool> interactable = null,
        IObservable<bool> visible = null,
        Action onClick = null)
    {
        // Allow rebinding
        if (clickListener != null && button != null)
        {
            button.onClick.RemoveListener(clickListener);
            clickListener = null;
        }

        disposables.Clear();

        if (label != null && labelText != null)
        {
            labelText
                .DistinctUntilChanged()
                .Subscribe(t => label.text = t)
                .AddTo(disposables);
        }

        if (interactable != null)
        {
            interactable
                .DistinctUntilChanged()
                .Subscribe(v => button.interactable = v)
                .AddTo(disposables);
        }

        if (visible != null)
        {
            visible
                .DistinctUntilChanged()
                .Subscribe(v => target.SetActive(v))
                .AddTo(disposables);
        }

        if (onClick != null)
        {
            // Helpful warning: if the Button has inspector-assigned click listeners, you'll get double actions.
            if (button != null && button.onClick != null && button.onClick.GetPersistentEventCount() > 0)
            {
                Debug.LogWarning(
                    "ReactiveButtonView: Button has persistent OnClick listeners assigned in the inspector. " +
                    "If you also bind an onClick action here, it will run in addition to those listeners.",
                    this
                );
            }

            clickListener = () => onClick();
            button.onClick.AddListener(clickListener);

            // Ensure the listener is removed when we rebind/dispose.
            Disposable.Create(() =>
            {
                if (button != null && clickListener != null)
                    button.onClick.RemoveListener(clickListener);

                clickListener = null;
            }).AddTo(disposables);
        }
    }

    private void OnDestroy() => disposables.Dispose();
}