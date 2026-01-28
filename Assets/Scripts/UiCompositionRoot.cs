using UnityEngine;

public sealed class UiCompositionRoot : MonoBehaviour
{
    [Header("Scene UI")]
    [SerializeField] private CurrencyView[] currencyViews;

    private bool isBound;

    public void Bind(in UiBindingsContext ctx)
    {
        if (isBound)
        {
            Debug.LogWarning("UiCompositionRoot: Bind called more than once. Ignoring.", this);
            return;
        }
        isBound = true;

        // Wallet HUD
        if (currencyViews != null)
        {
            foreach (var v in currencyViews)
            {
                if (v == null) continue;
                v.Initialize(ctx.WalletVM);
            }
        }

        // Intentionally no buttons yet. We’ll add BottomBarViewModel + UiCommand later.
    }
}