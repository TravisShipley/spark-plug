using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public sealed class PrototypeLaunchButton : MonoBehaviour
{
    [SerializeField]
    private GameSessionConfigAsset sessionConfig;

    [SerializeField]
    private Button button;

    [SerializeField]
    private TMP_Text label;

    [SerializeField]
    private bool useSessionDisplayName = true;

    [SerializeField]
    private string labelOverride;

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);

        RefreshLabel();
    }

    private void OnEnable()
    {
        if (button != null)
            button.onClick.AddListener(HandleClick);

        RefreshLabel();
        RefreshInteractable();
    }

    private void OnDisable()
    {
        if (button != null)
            button.onClick.RemoveListener(HandleClick);
    }

    public void SetSessionConfig(GameSessionConfigAsset config)
    {
        sessionConfig = config;
        RefreshLabel();
        RefreshInteractable();
    }

    private void HandleClick()
    {
        PrototypeLaunchService.Launch(sessionConfig);
    }

    private void RefreshLabel()
    {
        if (label == null)
            return;

        label.text = ResolveLabel();
    }

    private void RefreshInteractable()
    {
        if (button != null)
            button.interactable = sessionConfig != null;
    }

    private string ResolveLabel()
    {
        if (!string.IsNullOrWhiteSpace(labelOverride))
            return labelOverride.Trim();

        if (useSessionDisplayName && sessionConfig != null)
        {
            if (!string.IsNullOrWhiteSpace(sessionConfig.DisplayName))
                return sessionConfig.DisplayName;
            if (!string.IsNullOrWhiteSpace(sessionConfig.SessionId))
                return sessionConfig.SessionId;
        }

        return string.Empty;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (button == null)
            button = GetComponent<Button>();
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);

        RefreshLabel();
        RefreshInteractable();
    }
#endif
}
