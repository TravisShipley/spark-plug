using System;
using TMPro;
using UniRx;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class NurseryNodeView : MonoBehaviour, INodeView
{
    private const float HoldSpawnIntervalSeconds = 0.1f;

    [SerializeField]
    private TextMeshProUGUI nameText;

    [SerializeField]
    private TextMeshProUGUI levelInfoText;

    [SerializeField]
    private Button levelButton;

    [SerializeField]
    private TextMeshProUGUI levelButtonLabel;

    [SerializeField]
    private HoldRepeatButtonBinder levelHoldBinder;

    [SerializeField]
    private Button spawnButton;

    [SerializeField]
    private TextMeshProUGUI spawnButtonLabel;

    [SerializeField]
    private GameObject cooldownContainer;

    [SerializeField]
    private Image cooldownFill;

    [SerializeField]
    private GameObject levelProgressContainer;

    [SerializeField]
    private Image levelProgressFill;

    private readonly CompositeDisposable disposables = new();

    private GeneratorViewModel viewModel;
    private ManualChargedNodeService nurseryService;
    private SpawnButtonPointerRelay spawnButtonRelay;
    private UnityAction levelButtonClickHandler;
    private UnityAction spawnButtonClickHandler;
    private bool canLevelUpCached;
    private bool isSpawnPressed;
    private bool spawnRepeatedThisPress;
    private bool suppressNextSpawnClick;
    private float holdSpawnElapsedSeconds;

    public void Bind(GeneratorViewModel vm)
    {
        if (vm == null)
            throw new ArgumentNullException(nameof(vm));

        if (!ResolveReferences())
            return;

        var uiServices = UiServiceRegistry.Instance;
        if (
            uiServices == null
            || !uiServices.TryGetManualChargedNode(vm.Id, out nurseryService)
            || nurseryService == null
        )
        {
            Debug.LogError(
                $"NurseryNodeView: ManualChargedNodeService is not registered for node '{vm.Id}'.",
                this
            );
            return;
        }

        disposables.Clear();
        ResetSpawnPress(clearSuppressedClick: true);

        viewModel = vm;
        canLevelUpCached = vm.CanLevelUp.Value;
        nameText.text = vm.DisplayName;

        BindLevelButton(vm);
        BindSpawnButton(vm);
        BindInfo(vm);
        EnsureSpawnButtonRelay();
    }

    private void Update()
    {
        if (!isSpawnPressed || nurseryService == null)
            return;

        holdSpawnElapsedSeconds += Time.unscaledDeltaTime;
        while (holdSpawnElapsedSeconds >= HoldSpawnIntervalSeconds)
        {
            holdSpawnElapsedSeconds -= HoldSpawnIntervalSeconds;
            if (nurseryService.TrySpawnOnce())
                spawnRepeatedThisPress = true;
        }
    }

    private void OnDisable()
    {
        ResetSpawnPress(clearSuppressedClick: true);
    }

    private void OnDestroy()
    {
        ResetSpawnPress(clearSuppressedClick: true);
        disposables.Dispose();

        if (spawnButtonRelay != null)
            spawnButtonRelay.SetOwner(null);
    }

    private bool ResolveReferences()
    {
        nameText ??= FindText("Name");
        levelInfoText ??= FindText("LevelInfo");

        var levelButtonRoot = FindChild("LevelUpButton");
        if (levelButton == null && levelButtonRoot != null)
            levelButton = levelButtonRoot.GetComponent<Button>();
        if (levelButtonLabel == null && levelButtonRoot != null)
            levelButtonLabel = levelButtonRoot.GetComponentInChildren<TextMeshProUGUI>(true);
        if (levelHoldBinder == null && levelButtonRoot != null)
            levelHoldBinder = levelButtonRoot.GetComponent<HoldRepeatButtonBinder>();

        var spawnButtonRoot = FindChild("CollectButton");
        if (spawnButton == null && spawnButtonRoot != null)
            spawnButton = spawnButtonRoot.GetComponent<Button>();
        if (spawnButtonLabel == null && spawnButtonRoot != null)
            spawnButtonLabel = spawnButtonRoot.GetComponentInChildren<TextMeshProUGUI>(true);

        cooldownContainer ??= FindObject("CooldownView");
        if (cooldownFill == null)
        {
            var cooldownRoot = FindChild("CooldownView");
            cooldownFill = cooldownRoot != null ? FindImage(cooldownRoot, "Fill") : null;
        }

        levelProgressContainer ??= FindObject("LevelProgress");
        if (levelProgressFill == null)
        {
            var progressRoot = FindChild("LevelProgress");
            levelProgressFill = progressRoot != null ? FindImage(progressRoot, "Fill") : null;
        }

        if (nameText == null)
            return Fail(nameof(nameText));
        if (levelInfoText == null)
            return Fail(nameof(levelInfoText));
        if (levelButton == null)
            return Fail(nameof(levelButton));
        if (levelButtonLabel == null)
            return Fail(nameof(levelButtonLabel));
        if (spawnButton == null)
            return Fail(nameof(spawnButton));
        if (spawnButtonLabel == null)
            return Fail(nameof(spawnButtonLabel));
        if (cooldownFill == null)
            return Fail(nameof(cooldownFill));
        if (levelProgressFill == null)
            return Fail(nameof(levelProgressFill));

        return true;
    }

    private void BindLevelButton(GeneratorViewModel vm)
    {
        vm.CanLevelUp.DistinctUntilChanged()
            .Subscribe(value => canLevelUpCached = value)
            .AddTo(disposables);

        Observable
            .CombineLatest(
                vm.IsOwned.DistinctUntilChanged(),
                vm.NextLevelCost.DistinctUntilChanged(),
                vm.BuyModeDisplayName.DistinctUntilChanged(),
                vm.LevelUpDisplayCost.DistinctUntilChanged(),
                (owned, buildCost, modeLabel, levelCost) =>
                    owned
                        ? $"Level Up {modeLabel}\n{Format.Currency(levelCost)}"
                        : $"Build\n{Format.Currency(buildCost)}"
            )
            .DistinctUntilChanged()
            .Subscribe(text => levelButtonLabel.text = text)
            .AddTo(disposables);

        Observable
            .CombineLatest(
                vm.IsOwned.DistinctUntilChanged(),
                vm.CanBuild.DistinctUntilChanged(),
                vm.CanLevelUp.DistinctUntilChanged(),
                (owned, canBuild, canLevelUp) => owned ? canLevelUp : canBuild
            )
            .DistinctUntilChanged()
            .Subscribe(interactable => levelButton.interactable = interactable)
            .AddTo(disposables);

        levelButtonClickHandler = () =>
        {
            if (!vm.IsOwned.Value)
            {
                vm.BuildCommand.Execute();
                return;
            }

            if (levelHoldBinder != null && levelHoldBinder.ConsumeSuppressNextClick())
                return;

            vm.LevelUpCommand.Execute();
        };
        levelButton.onClick.AddListener(levelButtonClickHandler);
        Disposable
            .Create(() =>
            {
                if (levelButton != null && levelButtonClickHandler != null)
                    levelButton.onClick.RemoveListener(levelButtonClickHandler);

                levelButtonClickHandler = null;
            })
            .AddTo(disposables);

        if (levelHoldBinder != null)
        {
            levelHoldBinder.Bind(
                canRepeat: () =>
                    vm.IsOwned.Value && canLevelUpCached && vm.CanContinueHoldLevelUp(),
                onRepeat: () => vm.TryLevelUpByModeCapped(int.MaxValue),
                onPressStarted: vm.BeginHoldLevelUp,
                onPressEnded: vm.EndHoldLevelUp
            );
        }
    }

    private void BindSpawnButton(GeneratorViewModel vm)
    {
        spawnButtonLabel.text = "Spawn\nTap / Hold";

        vm.IsOwned.DistinctUntilChanged()
            .Subscribe(owned =>
            {
                spawnButton.interactable = owned;
                if (cooldownContainer != null)
                    cooldownContainer.SetActive(owned);
            })
            .AddTo(disposables);

        nurseryService
            .ChargeNormalized.DistinctUntilChanged()
            .Subscribe(fill => cooldownFill.fillAmount = Mathf.Clamp01(fill))
            .AddTo(disposables);

        spawnButtonClickHandler = () =>
        {
            if (suppressNextSpawnClick)
            {
                suppressNextSpawnClick = false;
                return;
            }

            if (!vm.IsOwned.Value)
                return;

            nurseryService.TrySpawnOnce();
        };
        spawnButton.onClick.AddListener(spawnButtonClickHandler);
        Disposable
            .Create(() =>
            {
                if (spawnButton != null && spawnButtonClickHandler != null)
                    spawnButton.onClick.RemoveListener(spawnButtonClickHandler);

                spawnButtonClickHandler = null;
            })
            .AddTo(disposables);
    }

    private void BindInfo(GeneratorViewModel vm)
    {
        Observable
            .CombineLatest(
                vm.Level.DistinctUntilChanged(),
                vm.NextMilestoneAtLevel.DistinctUntilChanged(),
                (currentLevel, nextLevel) => nextLevel > 0 ? $"{currentLevel} / {nextLevel}" : "Max"
            )
            .DistinctUntilChanged()
            .Subscribe(text => levelInfoText.text = text)
            .AddTo(disposables);

        vm.MilestoneProgressRatio.DistinctUntilChanged()
            .Subscribe(fill => levelProgressFill.fillAmount = Mathf.Clamp01(fill))
            .AddTo(disposables);

        vm.IsOwned.DistinctUntilChanged()
            .Subscribe(owned =>
            {
                if (levelProgressContainer != null)
                    levelProgressContainer.SetActive(owned);
            })
            .AddTo(disposables);
    }

    private void BeginSpawnPress()
    {
        if (viewModel == null || nurseryService == null || !viewModel.IsOwned.Value)
            return;

        suppressNextSpawnClick = false;
        isSpawnPressed = true;
        spawnRepeatedThisPress = false;
        holdSpawnElapsedSeconds = 0f;
        nurseryService.SetRefillPaused(true);
    }

    private void EndSpawnPress(bool cancelTap)
    {
        if (!isSpawnPressed)
            return;

        isSpawnPressed = false;
        holdSpawnElapsedSeconds = 0f;
        nurseryService?.SetRefillPaused(false);

        suppressNextSpawnClick = !cancelTap && spawnRepeatedThisPress;
        spawnRepeatedThisPress = false;
    }

    private void ResetSpawnPress(bool clearSuppressedClick)
    {
        isSpawnPressed = false;
        holdSpawnElapsedSeconds = 0f;
        spawnRepeatedThisPress = false;
        nurseryService?.SetRefillPaused(false);

        if (clearSuppressedClick)
            suppressNextSpawnClick = false;
    }

    private void EnsureSpawnButtonRelay()
    {
        if (spawnButton == null)
            return;

        spawnButtonRelay = spawnButton.GetComponent<SpawnButtonPointerRelay>();
        if (spawnButtonRelay == null)
            spawnButtonRelay = spawnButton.gameObject.AddComponent<SpawnButtonPointerRelay>();

        spawnButtonRelay.SetOwner(this);
    }

    private TextMeshProUGUI FindText(string objectName)
    {
        var target = FindChild(objectName);
        return target != null ? target.GetComponent<TextMeshProUGUI>() : null;
    }

    private GameObject FindObject(string objectName)
    {
        var target = FindChild(objectName);
        return target != null ? target.gameObject : null;
    }

    private Image FindImage(Transform root, string objectName)
    {
        var target = FindChild(root, objectName);
        return target != null ? target.GetComponent<Image>() : null;
    }

    private Transform FindChild(string objectName)
    {
        return FindChild(transform, objectName);
    }

    private static Transform FindChild(Transform root, string objectName)
    {
        if (root == null)
            return null;

        if (string.Equals(root.name, objectName, StringComparison.Ordinal))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            var result = FindChild(root.GetChild(i), objectName);
            if (result != null)
                return result;
        }

        return null;
    }

    private bool Fail(string fieldName)
    {
        Debug.LogError($"NurseryNodeView: Missing required ref '{fieldName}'.", this);
        return false;
    }

    private sealed class SpawnButtonPointerRelay
        : MonoBehaviour,
            IPointerDownHandler,
            IPointerUpHandler,
            IPointerExitHandler,
            ICancelHandler
    {
        private NurseryNodeView owner;

        public void SetOwner(NurseryNodeView owner)
        {
            this.owner = owner;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            owner?.BeginSpawnPress();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            owner?.EndSpawnPress(cancelTap: false);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            owner?.EndSpawnPress(cancelTap: true);
        }

        public void OnCancel(BaseEventData eventData)
        {
            owner?.EndSpawnPress(cancelTap: true);
        }
    }
}
