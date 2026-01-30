using UnityEngine;

public sealed class UiCompositionRoot : MonoBehaviour
{
    [Header("Scene UI")]
    [SerializeField] private CurrencyView[] currencyViews;
    [SerializeField] private BottomBarView bottomBarView;

    private bool hasBound;

    public void Bind(in UiBindingsContext ctx)
    {
        if (!TryBeginBind())
            return;

        if (!Validate(ctx))
            return;

        BindWalletHud(ctx);
        BindBottomBar(ctx);
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

    private bool Validate(in UiBindingsContext ctx)
    {
        if (bottomBarView == null)
        {
            Debug.LogError("UiCompositionRoot: BottomBarView is not assigned.", this);
            return false;
        }

        if (ctx.WalletViewModel == null)
        {
            Debug.LogError("UiCompositionRoot: WalletViewModel is null in UiBindingsContext.", this);
            return false;
        }

        if (ctx.ModalService == null)
        {
            Debug.LogError("UiCompositionRoot: ModalService is null in UiBindingsContext.", this);
            return false;
        }

        return true;
    }

    private void BindWalletHud(in UiBindingsContext ctx)
    {
        if (currencyViews == null || currencyViews.Length == 0)
            return;

        foreach (var v in currencyViews)
        {
            if (v == null)
                continue;

            v.Initialize(ctx.WalletViewModel);
        }
    }

    private void BindBottomBar(in UiBindingsContext ctx)
    {
        var bottomBarVm = new BottomBarViewModel(ctx.ModalService);
        bottomBarView.Bind(bottomBarVm);
    }
}