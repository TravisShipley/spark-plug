using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class UiScreenManager : MonoBehaviour, IGeneratorLookup
{
    // Inspector helper for modal registry
    [Serializable]
    private sealed class ModalEntry
    {
        public string Id;
        public UiScreenView Prefab;
    }

    [Header("Services")]
    [SerializeField]
    private UiServiceRegistry uiServices;

    [Header("Scene References")]
    [SerializeField]
    private Transform modalContainer;

    [SerializeField]
    private Button backdropButton; // full-screen button behind modal(s)

    [SerializeField]
    private CanvasGroup backdropGroup;

    [SerializeField]
    private Canvas backdropCanvas;

    [Header("Behavior")]
    [SerializeField]
    private bool closeOnEscape = true;

    [SerializeField]
    private bool closeOnBackdropClick = true;

    [Header("Stacking")]
    [SerializeField]
    private int baseModalSortingOrder = 100;

    [SerializeField]
    private int sortingStep = 10;

    [Header("Modal Registry")]
    [SerializeField]
    private List<ModalEntry> modalPrefabs = new();

    private readonly Stack<UiScreenView> stack = new();
    private Dictionary<string, UiScreenView> modalById;

    public UpgradeService UpgradeService { get; private set; }
    public UpgradeCatalog UpgradeCatalog { get; set; }
    public GameDefinitionService GameDefinitionService { get; set; }

    public void Initialize(UpgradeService upgradeService)
    {
        UpgradeService = upgradeService;
    }

    public bool TryGetGenerator(string generatorId, out GeneratorService gen)
    {
        gen = null;
        return uiServices != null && uiServices.TryGetGenerator(generatorId, out gen);
    }

    private void Awake()
    {
        if (modalContainer == null)
            modalContainer = transform;

        if (uiServices == null)
        {
            Debug.LogError(
                "UiScreenManager: UiServiceRegistry is not assigned. Assign it in the inspector so screens can resolve generators.",
                this
            );
        }

        if (backdropButton != null)
            backdropButton.onClick.AddListener(OnBackdropClicked);

        if (backdropCanvas == null)
        {
            if (backdropGroup != null)
                backdropCanvas = backdropGroup.GetComponentInParent<Canvas>(true);

            if (backdropCanvas == null && backdropButton != null)
                backdropCanvas = backdropButton.GetComponentInParent<Canvas>(true);
        }

        modalById = new Dictionary<string, UiScreenView>(StringComparer.Ordinal);

        foreach (var entry in modalPrefabs)
        {
            if (entry == null || entry.Prefab == null)
                continue;

            var id = (entry.Id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning(
                    $"UiScreenManager: ScreenEntry with empty Id on prefab '{entry.Prefab.name}' was ignored.",
                    this
                );
                continue;
            }

            if (modalById.ContainsKey(id))
            {
                Debug.LogWarning(
                    $"UiScreenManager: Duplicate screen Id '{id}' found. Keeping the first, ignoring '{entry.Prefab.name}'.",
                    this
                );
                continue;
            }

            modalById.Add(id, entry.Prefab);
        }

        RefreshStack();
    }

    private void Update()
    {
        if (!closeOnEscape)
            return;

        // Esc on desktop; Android back often maps to Escape too.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (stack.Count == 0)
                return;

            var top = stack.Peek();
            if (top != null && top.Dismissible && top.CloseOnEscape)
                CloseTop();
        }
    }

    private void EnsureUpgradeServiceInitializedIfNeeded()
    {
        // Awake order is not guaranteed; only validate when a modal is actually being shown.
        if (UpgradeService == null)
            return;

        if (UpgradeService.Wallet != null)
            return;

        Debug.LogError(
            "UiScreenManager: UpgradeService.Wallet is null. Ensure UpgradeService is constructed with a WalletService during bootstrap.",
            this
        );
    }

    // Inspector-facing method
    public void ShowById(string id)
    {
        EnsureUpgradeServiceInitializedIfNeeded();
        Show(id);
    }

    public T Show<T>(T modalPrefab, object payload = null)
        where T : UiScreenView
    {
        if (modalPrefab == null)
            throw new ArgumentNullException(nameof(modalPrefab));

        EnsureUpgradeServiceInitializedIfNeeded();

        var instance = Instantiate(modalPrefab, modalContainer);
        instance.gameObject.SetActive(true);

        instance.Manager = this;

        instance.OnBeforeShow(payload);
        stack.Push(instance);

        RefreshStack();
        instance.OnShown();

        return (T)instance;
    }

    public UiScreenView Show(string id, object payload = null)
    {
        if (modalById == null)
            throw new InvalidOperationException("UiScreenManager has not been initialized.");

        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Modal id is null or empty.", nameof(id));

        id = id.Trim();

        if (!modalById.TryGetValue(id, out var prefab) || prefab == null)
            throw new KeyNotFoundException(
                $"UiScreenManager: No screen registered with id '{id}'."
            );

        return Show(prefab, payload);
    }

    public bool TryCloseTop()
    {
        if (stack.Count == 0)
            return false;

        var top = stack.Peek();
        if (top != null && !top.Dismissible)
            return false;

        CloseTop();
        return true;
    }

    public void CloseTop()
    {
        if (stack.Count == 0)
            return;

        var top = stack.Pop();
        if (top != null)
        {
            top.OnBeforeClose();
            Destroy(top.gameObject);
        }

        RefreshStack();
    }

    public void CloseAll()
    {
        while (stack.Count > 0)
        {
            var top = stack.Pop();
            if (top != null)
            {
                top.OnBeforeClose();
                Destroy(top.gameObject);
            }
        }

        RefreshStack();
    }

    private void OnBackdropClicked()
    {
        if (!closeOnBackdropClick)
            return;
        if (stack.Count == 0)
            return;

        var top = stack.Peek();
        if (top != null && top.Dismissible && top.CloseOnBackdrop)
            CloseTop();
    }

    private void RefreshStack()
    {
        bool any = stack.Count > 0;

        // Keep backdrop just below the top-most modal.
        if (backdropCanvas != null)
        {
            backdropCanvas.overrideSorting = true;

            if (stack.Count == 0)
            {
                backdropCanvas.sortingOrder = baseModalSortingOrder - 1;
            }
            else
            {
                // Top modal uses baseModalSortingOrder + (stack.Count - 1) * sortingStep
                int topSortingOrder = baseModalSortingOrder + (stack.Count - 1) * sortingStep;
                backdropCanvas.sortingOrder = topSortingOrder - 1;
            }
        }

        if (backdropButton != null)
            backdropButton.gameObject.SetActive(any);

        if (backdropGroup != null)
        {
            backdropGroup.alpha = any ? 1f : 0f;
            backdropGroup.blocksRaycasts = any;
            backdropGroup.interactable = any;
        }

        // Ensure only the top modal is interactable and assign sorting orders.
        // Stack enumerates from top -> bottom.
        int i = 0;
        foreach (var modal in stack)
        {
            if (modal == null)
            {
                i++;
                continue;
            }

            bool isTop = (i == 0);

            // Sorting: bottom = base, then +step per layer.
            int sortingOrder = baseModalSortingOrder + (stack.Count - 1 - i) * sortingStep;

            var canvas = modal.Canvas;
            if (canvas != null && canvas.overrideSorting)
                canvas.sortingOrder = sortingOrder;

            var cg = modal.CanvasGroup;
            if (cg != null)
            {
                cg.blocksRaycasts = isTop;
                cg.interactable = isTop;
            }

            i++;
        }
    }
}
