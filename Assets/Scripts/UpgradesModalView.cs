using System;
using System.Collections.Generic;
using TMPro;
using UniRx;
using UnityEngine;


public sealed class UpgradesModalView : ModalView
{
    [Header("UI")]
    [SerializeField] private Transform listContainer;
    [SerializeField] private UpgradeEntryView entryPrefab;

    [Header("Data")]
    [SerializeField] private UpgradeDatabase upgradeDatabase;

    // Local state (v1): one-time upgrades tracked in-memory.
    // Next step is to persist this to GameData.
    private readonly Dictionary<string, ReactiveProperty<bool>> purchasedById = new Dictionary<string, ReactiveProperty<bool>>(StringComparer.Ordinal);

    private readonly CompositeDisposable disposables = new CompositeDisposable();

    public override void OnBeforeShow(object payload)
    {
        disposables.Clear();
        ClearList();

        if (upgradeDatabase == null)
        {
            Debug.LogError("UpgradesModalView: UpgradeDatabase is not assigned.", this);
            return;
        }

        if (entryPrefab == null || listContainer == null)
        {
            Debug.LogError("UpgradesModalView: entryPrefab or listContainer is not assigned.", this);
            return;
        }

        var ctx = UpgradesContext;
        if (ctx == null)
        {
            Debug.LogError("UpgradesModalView: No IUpgradesContext is available (check ModalManager/UiServiceRegistry wiring).", this);
            return;
        }

        if (ctx.Wallet == null)
        {
            Debug.LogError("UpgradesModalView: UpgradesContext has a null WalletService (did you call Initialize?).", this);
            return;
        }

        // Show all upgrades in the database. Each entry wires itself to the generator specified by upgrade.GeneratorId.
        foreach (var upgrade in upgradeDatabase.Upgrades)
        {
            if (upgrade == null) continue;

            string genId = (upgrade.GeneratorId ?? string.Empty).Trim();

            // For v1, require a GeneratorId so we can wire it. (Global upgrades can be added later.)
            if (string.IsNullOrEmpty(genId))
                continue;

            if (!ctx.TryGetGenerator(genId, out var generator) || generator == null)
            {
                Debug.LogWarning($"UpgradesModalView: No generator found for GeneratorId '{genId}' (upgrade '{upgrade.Id}').", this);
                continue;
            }

            var entry = Instantiate(entryPrefab, listContainer);
            entry.name = $"Upgrade_{upgrade.Id}";

            entry.Bind(
                upgrade,
                generator,
                ctx.Wallet,
                IsPurchased(upgrade.Id),
                () => MarkPurchased(upgrade.Id)
            );
        }
    }

    private IReadOnlyReactiveProperty<bool> IsPurchased(string id)
    {
        id = (id ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(id))
            return new ReactiveProperty<bool>(false);

        if (!purchasedById.TryGetValue(id, out var rp) || rp == null)
        {
            rp = new ReactiveProperty<bool>(false);
            purchasedById[id] = rp;
        }

        return rp;
    }

    private void MarkPurchased(string id)
    {
        id = (id ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(id))
            return;

        if (!purchasedById.TryGetValue(id, out var rp) || rp == null)
        {
            rp = new ReactiveProperty<bool>(true);
            purchasedById[id] = rp;
        }
        else
        {
            rp.Value = true;
        }
    }

    private void ClearList()
    {
        if (listContainer == null) return;

        for (int i = listContainer.childCount - 1; i >= 0; i--)
        {
            var child = listContainer.GetChild(i);
            if (child != null)
                Destroy(child.gameObject);
        }
    }

    private void OnDestroy()
    {
        disposables.Dispose();

        foreach (var kv in purchasedById)
            kv.Value?.Dispose();

        purchasedById.Clear();
    }
}
