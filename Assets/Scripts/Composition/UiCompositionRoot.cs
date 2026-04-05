/*
 * UiCompositionRoot
 * -----------------
 * Scene-level UI composition root responsible for binding runtime UI views
 * to their corresponding view-models and services.
 *
 * Responsibilities:
 * - Validate required UI scene references
 * - Perform one-time binding of UI views to view-models
 * - Bind wallet-related HUD elements (currency displays)
 * - Construct and bind bottom bar UI and its view-model
 *
 * Design notes:
 * - This class performs UI wiring only; it contains no game logic or state.
 * - Binding is intentionally one-shot per scene lifetime to prevent duplicate
 *   subscriptions or unintended side effects.
 * - Receives all dependencies via UiBindingsContext to keep UI decoupled from
 *   game composition and initialization order.
 */
using UnityEngine;
using UnityEngine.Serialization;

public sealed class UiCompositionRoot : MonoBehaviour
{
    [Header("Scene UI")]
    [SerializeField]
    private CurrencyView[] currencyViews;

    [SerializeField]
    private ResourceView[] resourceViews;

    [SerializeField]
    private BottomBarView bottomBarView;

    [SerializeField]
    private TopBarView topBarView;

    [FormerlySerializedAs("nodeView")]
    [SerializeField]
    private TestPanelView testPanel;

    [SerializeField]
    private AdBoostButtonView[] adBoostButtons;

    [SerializeField]
    private BuyModeButtonView[] buyModeButtons;

    [SerializeField]
    private LlamaHudView llamaHudView;

    private bool hasBound;
    private BuyModeViewModel buyModeViewModel;
    private LlamaHudViewModel llamaHudViewModel;
    private TestPanelViewModel testPanelViewModel;
    private TopBarViewModel topBarViewModel;

    public void Bind(in UiBindingsContext context)
    {
        if (!TryBeginBind())
            return;

        if (!Validate(context))
            return;

        BindWalletHud(context);
        BindTopBar(context);
        BindBottomBar(context);
        BindLlamaHud(context);
        BindTestPanel();
        BindAdBoostButtons(context);
        BindBuyModeButtons(context);
    }

    private bool TryBeginBind()
    {
        if (hasBound)
        {
            Debug.LogWarning("UiCompositionRoot: Bind called more than once. Ignoring.", this);
            return false;
        }

        hasBound = true;
        return true;
    }

    private bool Validate(in UiBindingsContext context)
    {
        if (topBarView == null)
            topBarView = GetComponentInChildren<TopBarView>(true);

        if (bottomBarView == null)
        {
            Debug.LogError("UiCompositionRoot: BottomBarView is not assigned.", this);
            return false;
        }

        if (topBarView == null)
        {
            Debug.LogError("UiCompositionRoot: TopBarView is not assigned.", this);
            return false;
        }

        if (context.WalletViewModel == null)
        {
            Debug.LogError(
                "UiCompositionRoot: WalletViewModel is null in UiBindingsContext.",
                this
            );
            return false;
        }

        if (context.UiScreenService == null)
        {
            Debug.LogError(
                "UiCompositionRoot: UiScreenService is null in UiBindingsContext.",
                this
            );
            return false;
        }

        if (context.UpgradeService == null)
        {
            Debug.LogError("UiCompositionRoot: UpgradeService is null in UiBindingsContext.", this);
            return false;
        }

        if (context.TimeWarpService == null)
        {
            Debug.LogError("UiCompositionRoot: TimeWarpService is null in UiBindingsContext.", this);
            return false;
        }

        return true;
    }

    private void BindWalletHud(in UiBindingsContext context)
    {
        if (currencyViews != null)
        {
            foreach (var v in currencyViews)
            {
                if (v == null)
                    continue;

                v.Initialize(context.WalletViewModel);
            }
        }

        if (resourceViews == null || resourceViews.Length == 0)
            return;

        foreach (var v in resourceViews)
        {
            if (v == null)
                continue;

            v.Initialize(context.WalletViewModel);
        }
    }

    private void BindTopBar(in UiBindingsContext context)
    {
        topBarViewModel ??= new TopBarViewModel(context.TimeWarpService);
        topBarView.Bind(topBarViewModel);
    }

    private void BindBottomBar(in UiBindingsContext context)
    {
        var bottomBarVm = new BottomBarViewModel(context.UiScreenService, context.UpgradeService);
        bottomBarView.Bind(bottomBarVm);
    }

    private void BindLlamaHud(in UiBindingsContext context)
    {
        if (llamaHudView == null)
            llamaHudView = GetComponentInChildren<LlamaHudView>(true);

        if (llamaHudView == null)
            return;

        if (context.StateVarService == null)
        {
            Debug.LogError("UiCompositionRoot: StateVarService is null in UiBindingsContext.", this);
            return;
        }

        llamaHudViewModel ??= new LlamaHudViewModel(context.StateVarService);
        llamaHudView.Initialize(llamaHudViewModel);
    }

    private void BindTestPanel()
    {
        if (testPanel == null)
            return;

        testPanelViewModel ??= new TestPanelViewModel(
            "Chicken coop",
            "$12.34M",
            0.67f,
            true,
            false
        );
        testPanel.Bind(testPanelViewModel);
    }

    private void BindAdBoostButtons(in UiBindingsContext context)
    {
        if (adBoostButtons == null || adBoostButtons.Length == 0)
            return;

        for (int i = 0; i < adBoostButtons.Length; i++)
        {
            var buttonView = adBoostButtons[i];
            if (buttonView == null)
                continue;

            buttonView.Bind(context.UiScreenService);
        }
    }

    private void BindBuyModeButtons(in UiBindingsContext context)
    {
        if (buyModeButtons == null || buyModeButtons.Length == 0)
            return;

        if (context.BuyModeService == null)
        {
            Debug.LogError("UiCompositionRoot: BuyModeService is null in UiBindingsContext.", this);
            return;
        }

        buyModeViewModel ??= new BuyModeViewModel(context.BuyModeService);
        for (int i = 0; i < buyModeButtons.Length; i++)
        {
            var buttonView = buyModeButtons[i];
            if (buttonView == null)
                continue;

            buttonView.Bind(buyModeViewModel);
        }
    }

    private void OnDestroy()
    {
        topBarViewModel?.Dispose();
        testPanelViewModel?.Dispose();
        buyModeViewModel?.Dispose();
        llamaHudViewModel?.Dispose();
    }
}
