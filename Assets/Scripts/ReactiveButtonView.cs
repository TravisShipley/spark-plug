using System;
using TMPro;
using UniRx;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ReactiveButtonView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private GameObject target; // optional; defaults to this GO

    private readonly CompositeDisposable disposables = new();

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
            button.OnClickAsObservable()
                .Subscribe(_ => onClick())
                .AddTo(disposables);
        }
    }

    private void OnDestroy() => disposables.Dispose();
}