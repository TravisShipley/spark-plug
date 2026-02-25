using System;
using UniRx;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class HoldRepeatButtonBinder
    : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerExitHandler,
        ICancelHandler
{
    [Header("Pointer Source (optional)")]
    [SerializeField]
    private GameObject pointerEventSource;

    [Header("Repeat Timing")]
    [SerializeField]
    private float initialDelaySeconds = 0.25f;

    [SerializeField]
    private float repeatIntervalSeconds = 0.08f;

    private Func<bool> canRepeat;
    private Action onRepeat;
    private Action onPressStarted;
    private Action onPressEnded;
    private IDisposable repeatLoop;
    private bool isPressed;
    private bool suppressNextClick;
    private bool hasRepeatedThisPress;
    private HoldRepeatPointerRelay pointerRelay;
    private GameObject pointerRelaySource;

    public void Bind(
        Func<bool> canRepeat,
        Action onRepeat,
        Action onPressStarted = null,
        Action onPressEnded = null
    )
    {
        this.canRepeat = canRepeat;
        this.onRepeat = onRepeat;
        this.onPressStarted = onPressStarted;
        this.onPressEnded = onPressEnded;
        EndPress(clearSuppressedClick: true);
        hasRepeatedThisPress = false;
        EnsurePointerRelay();
    }

    public bool ConsumeSuppressNextClick()
    {
        if (!suppressNextClick)
            return false;

        suppressNextClick = false;
        return true;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        BeginPress();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (hasRepeatedThisPress)
            suppressNextClick = true;

        EndPress(clearSuppressedClick: false);
        hasRepeatedThisPress = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        EndPress(clearSuppressedClick: true);
        hasRepeatedThisPress = false;
    }

    public void OnCancel(BaseEventData eventData)
    {
        EndPress(clearSuppressedClick: true);
        hasRepeatedThisPress = false;
    }

    private void OnDisable()
    {
        EndPress(clearSuppressedClick: true);
        hasRepeatedThisPress = false;
    }

    private void OnDestroy()
    {
        EndPress(clearSuppressedClick: true);
        hasRepeatedThisPress = false;

        if (pointerRelay != null)
        {
            pointerRelay.SetOwner(null);
            pointerRelay = null;
            pointerRelaySource = null;
        }
    }

    private void BeginPress()
    {
        EndPress(clearSuppressedClick: true);
        hasRepeatedThisPress = false;
        onPressStarted?.Invoke();
        isPressed = true;
        StartRepeatLoop();
    }

    private void EndPress(bool clearSuppressedClick)
    {
        var wasPressed = isPressed;
        isPressed = false;
        StopRepeatLoop();
        if (wasPressed)
            onPressEnded?.Invoke();

        if (clearSuppressedClick)
            suppressNextClick = false;
    }

    private void StartRepeatLoop()
    {
        StopRepeatLoop();

        var delay = Math.Max(0f, initialDelaySeconds);
        var interval = Math.Max(0.01f, repeatIntervalSeconds);

        repeatLoop = Observable
            .Timer(
                TimeSpan.FromSeconds(delay),
                TimeSpan.FromSeconds(interval),
                Scheduler.MainThreadIgnoreTimeScale
            )
            .Subscribe(_ => TickRepeat());
    }

    private void StopRepeatLoop()
    {
        repeatLoop?.Dispose();
        repeatLoop = null;
    }

    private void TickRepeat()
    {
        if (!isPressed)
        {
            StopRepeatLoop();
            return;
        }

        if (canRepeat == null || onRepeat == null)
        {
            EndPress(clearSuppressedClick: true);
            return;
        }

        if (!canRepeat())
        {
            EndPress(clearSuppressedClick: false);
            return;
        }

        onRepeat();
        hasRepeatedThisPress = true;

        if (!canRepeat())
            EndPress(clearSuppressedClick: false);
    }

    private void EnsurePointerRelay()
    {
        var source = pointerEventSource != null ? pointerEventSource : gameObject;
        if (source == gameObject)
        {
            if (pointerRelay != null)
            {
                pointerRelay.SetOwner(null);
                pointerRelay = null;
                pointerRelaySource = null;
            }

            return;
        }

        if (pointerRelay != null && pointerRelaySource == source)
        {
            pointerRelay.SetOwner(this);
            return;
        }

        if (pointerRelay != null)
            pointerRelay.SetOwner(null);

        pointerRelay = source.GetComponent<HoldRepeatPointerRelay>();
        if (pointerRelay == null)
            pointerRelay = source.AddComponent<HoldRepeatPointerRelay>();

        pointerRelay.SetOwner(this);
        pointerRelaySource = source;
    }

    private sealed class HoldRepeatPointerRelay
        : MonoBehaviour,
            IPointerDownHandler,
            IPointerUpHandler,
            IPointerExitHandler,
            ICancelHandler
    {
        private HoldRepeatButtonBinder owner;

        public void SetOwner(HoldRepeatButtonBinder owner)
        {
            this.owner = owner;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            owner?.OnPointerDown(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            owner?.OnPointerUp(eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            owner?.OnPointerExit(eventData);
        }

        public void OnCancel(BaseEventData eventData)
        {
            owner?.OnCancel(eventData);
        }
    }
}
