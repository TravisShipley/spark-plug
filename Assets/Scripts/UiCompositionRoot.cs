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

public sealed class UiCompositionRoot : MonoBehaviour
{
    [Header("Scene UI")]
    [SerializeField]
    private CurrencyView[] currencyViews;

    [SerializeField]
    private BottomBarView bottomBarView;

    private bool hasBound;

    public void Bind(in UiBindingsContext context)
    {
        if (!TryBeginBind())
            return;

        if (!Validate(context))
            return;

        BindWalletHud(context);
        BindBottomBar(context);
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
        if (bottomBarView == null)
        {
            Debug.LogError("UiCompositionRoot: BottomBarView is not assigned.", this);
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

        return true;
    }

    private void BindWalletHud(in UiBindingsContext context)
    {
        if (currencyViews == null || currencyViews.Length == 0)
            return;

        foreach (var v in currencyViews)
        {
            if (v == null)
                continue;

            v.Initialize(context.WalletViewModel);
        }
    }

    private void BindBottomBar(in UiBindingsContext context)
    {
        var bottomBarVm = new BottomBarViewModel(context.UiScreenService, context.UpgradeService);
        bottomBarView.Bind(bottomBarVm);
    }
}
