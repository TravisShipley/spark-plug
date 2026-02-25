using System;
using UniRx;
using UnityEngine;

/*
Manual test checklist:
1) Start game and bind a BuyModeButtonView to this service.
2) Click mode toggle and verify label cycles x1/x10/Next/Max.
3) Press Level Up with x1 and verify +1 level.
4) Press Level Up with x10 and verify up to +10 levels (or fewer when unaffordable).
5) Press Level Up with Next and verify it stops exactly at next milestone (or +1 if no next milestone).
6) Press Level Up with Max and verify it buys as many levels as currently affordable.
7) Hold Level Up and release; verify auto-buy stops immediately on release.
*/
public sealed class BuyModeService : IDisposable
{
    private readonly BuyModeCatalog catalog;
    private readonly ReactiveProperty<string> selectedBuyModeId = new(string.Empty);
    private readonly ReactiveProperty<BuyModeDefinition> selectedBuyMode = new();

    public IReadOnlyReactiveProperty<string> SelectedBuyModeId => selectedBuyModeId;
    public IReadOnlyReactiveProperty<BuyModeDefinition> SelectedBuyMode => selectedBuyMode;

    public BuyModeService(BuyModeCatalog catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        var defaultMode = this.catalog.GetDefault();
        SetSelected(defaultMode);
    }

    public void SetBuyMode(string id)
    {
        var normalizedId = NormalizeId(id);
        if (string.IsNullOrEmpty(normalizedId))
            throw new InvalidOperationException("BuyModeService: buy mode id is empty.");

        if (catalog.TryGet(normalizedId, out var definition) && definition != null)
        {
            SetSelected(definition);
            return;
        }

        throw new InvalidOperationException(
            $"BuyModeService: unknown buy mode id '{normalizedId}'."
        );
    }

    public void CycleNext()
    {
        var all = catalog.All;
        if (all == null || all.Count == 0)
        {
            SetSelected(catalog.GetDefault());
            return;
        }

        var currentId = NormalizeId(selectedBuyModeId.Value);
        var currentIndex = -1;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] == null)
                continue;

            if (string.Equals(NormalizeId(all[i].id), currentId, StringComparison.Ordinal))
            {
                currentIndex = i;
                break;
            }
        }

        var nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % all.Count;
        var next = all[nextIndex] ?? catalog.GetDefault();
        SetSelected(next);
    }

    public void Dispose()
    {
        selectedBuyModeId.Dispose();
        selectedBuyMode.Dispose();
    }

    private void SetSelected(BuyModeDefinition definition)
    {
        if (definition == null)
            throw new InvalidOperationException("BuyModeService: selected buy mode is null.");

        var id = NormalizeId(definition.id);
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("BuyModeService: selected buy mode id is empty.");

        var displayName = (definition.displayName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(displayName))
            displayName = id;

        selectedBuyModeId.Value = id;
        selectedBuyMode.Value = definition;

        Debug.Log($"[BuyMode] Selected: {id} ({displayName})");
    }

    private static string NormalizeId(string raw)
    {
        return (raw ?? string.Empty).Trim();
    }
}
