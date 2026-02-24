using System;
using System.Collections.Generic;
using UniRx;
using UnityEngine;

/// <summary>
/// Owns an in-memory GameData snapshot and performs debounced writes to disk.
/// SaveService is the only runtime component that talks to SaveSystem.
/// </summary>
public sealed class SaveService : IDisposable
{
    private readonly CompositeDisposable disposables = new();
    private readonly Subject<Unit> saveRequests = new();
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);
    private bool hasPendingScopedResetReload;

    public GameData Data { get; private set; }
    public long LastSeenUnixSeconds => Data?.lastSeenUnixSeconds ?? 0;
    public string ActiveBuffId => NormalizeId(Data?.ActiveBuffId);
    public long ActiveBuffExpiresAtUnixSeconds => Data?.ActiveBuffExpiresAtUnixSeconds ?? 0;

    public SaveService()
    {
        saveRequests
            .Throttle(Debounce)
            .Subscribe(_ =>
            {
                if (Data != null)
                    SaveSystem.SaveGame(Data);
            })
            .AddTo(disposables);
    }

    public void Load(GameDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        var hadSave = SaveSystem.HasSave();
        var loaded = hadSave ? SaveSystem.LoadGame() : null;

        bool changed;
        Data = BuildFromDefaults(definition, loaded, out changed);
        hasPendingScopedResetReload = false;

        if (!hadSave || loaded == null || changed)
            SaveNow();
    }

    public void Reset(GameDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        Data = CreateDefaultSaveData(definition);
        hasPendingScopedResetReload = false;
        SaveSystem.DeleteSaveFile();
        SaveNow();
    }

    public void RequestSave()
    {
        if (Data == null)
            return;

        saveRequests.OnNext(Unit.Default);
    }

    public void SaveNow()
    {
        if (Data == null)
            return;

        SortFactLists();
        SaveSystem.SaveGame(Data);
    }

    public void SetLastSeenUnixSeconds(long unixSeconds, bool requestSave = true)
    {
        EnsureDataInitialized();

        var normalized = Math.Max(0, unixSeconds);
        if (Data.lastSeenUnixSeconds == normalized)
            return;

        Data.lastSeenUnixSeconds = normalized;
        if (requestSave)
            RequestSave();
    }

    public void SetActiveBuffState(string buffId, long expiresAtUnixSeconds, bool requestSave = true)
    {
        var id = NormalizeId(buffId);
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("SaveService.SetActiveBuffState: buffId is empty.");

        EnsureDataInitialized();
        var normalizedExpiresAt = Math.Max(0, expiresAtUnixSeconds);

        bool changed = false;
        if (!string.Equals(Data.ActiveBuffId, id, StringComparison.Ordinal))
        {
            Data.ActiveBuffId = id;
            changed = true;
        }

        if (Data.ActiveBuffExpiresAtUnixSeconds != normalizedExpiresAt)
        {
            Data.ActiveBuffExpiresAtUnixSeconds = normalizedExpiresAt;
            changed = true;
        }

        if (changed && requestSave)
            RequestSave();
    }

    public void ClearActiveBuffState(bool requestSave = true)
    {
        EnsureDataInitialized();
        bool changed = false;

        if (!string.IsNullOrEmpty(Data.ActiveBuffId))
        {
            Data.ActiveBuffId = string.Empty;
            changed = true;
        }

        if (Data.ActiveBuffExpiresAtUnixSeconds != 0)
        {
            Data.ActiveBuffExpiresAtUnixSeconds = 0;
            changed = true;
        }

        if (changed && requestSave)
            RequestSave();
    }

    public void SetResourceBalance(string resourceId, double amount, bool requestSave = true)
    {
        var id = NormalizeId(resourceId);
        if (string.IsNullOrEmpty(id))
            return;

        EnsureDataInitialized();
        Data.Resources ??= new List<GameData.ResourceBalanceData>();

        var entry = Data.Resources.Find(
            e => e != null && string.Equals(NormalizeId(e.ResourceId), id, StringComparison.Ordinal)
        );

        if (entry == null)
        {
            Data.Resources.Add(new GameData.ResourceBalanceData { ResourceId = id, Amount = amount });
            if (requestSave)
                RequestSave();
            return;
        }

        if (Math.Abs(entry.Amount - amount) < 0.0000001d)
            return;

        entry.Amount = amount;
        if (requestSave)
            RequestSave();
    }

    public double GetLifetimeEarnings(string resourceId)
    {
        var id = NormalizeId(resourceId);
        if (string.IsNullOrEmpty(id))
            return 0d;

        EnsureDataInitialized();
        Data.LifetimeEarnings ??= new List<GameData.LifetimeEarningData>();

        var entry = Data.LifetimeEarnings.Find(
            e => e != null && string.Equals(NormalizeId(e.ResourceId), id, StringComparison.Ordinal)
        );

        return entry?.Amount ?? 0d;
    }

    public void AddLifetimeEarnings(string resourceId, double amount, bool requestSave = true)
    {
        var id = NormalizeId(resourceId);
        if (string.IsNullOrEmpty(id))
            return;

        if (double.IsNaN(amount) || double.IsInfinity(amount))
            throw new InvalidOperationException(
                $"SaveService.AddLifetimeEarnings: invalid amount '{amount}' for resource '{id}'."
            );

        if (amount <= 0d)
            return;

        EnsureDataInitialized();
        Data.LifetimeEarnings ??= new List<GameData.LifetimeEarningData>();

        var entry = Data.LifetimeEarnings.Find(
            e => e != null && string.Equals(NormalizeId(e.ResourceId), id, StringComparison.Ordinal)
        );

        if (entry == null)
        {
            Data.LifetimeEarnings.Add(
                new GameData.LifetimeEarningData { ResourceId = id, Amount = amount }
            );
            if (requestSave)
                RequestSave();
            return;
        }

        entry.Amount += amount;
        if (requestSave)
            RequestSave();
    }

    public void SetNodeInstanceState(
        string nodeInstanceId,
        bool owned,
        bool enabled,
        int level,
        bool automationPurchased,
        bool requestSave = true
    )
    {
        var id = NormalizeId(nodeInstanceId);
        if (string.IsNullOrEmpty(id))
            return;

        EnsureDataInitialized();
        Data.Generators ??= new List<GameData.GeneratorStateData>();
        Data.UnlockedNodeInstanceIds ??= new HashSet<string>(StringComparer.Ordinal);

        if (automationPurchased)
        {
            owned = true;
            enabled = true;
        }

        if (owned)
            enabled = true;

        level = Math.Max(0, level);
        if (owned && level < 1)
            level = 1;
        if (!owned)
            level = 0;

        var entry = Data.Generators.Find(
            g => g != null && string.Equals(NormalizeId(g.Id), id, StringComparison.Ordinal)
        );

        bool changed = false;
        if (entry == null)
        {
            entry = new GameData.GeneratorStateData { Id = id };
            Data.Generators.Add(entry);
            changed = true;
        }

        if (entry.IsOwned != owned)
        {
            entry.IsOwned = owned;
            changed = true;
        }

        if (entry.IsEnabled != enabled)
        {
            entry.IsEnabled = enabled;
            changed = true;
        }

        if (entry.Level != level)
        {
            entry.Level = level;
            changed = true;
        }

        if (entry.IsAutomationPurchased != automationPurchased)
        {
            entry.IsAutomationPurchased = automationPurchased;
            changed = true;
        }

        if (entry.IsAutomated != automationPurchased)
        {
            entry.IsAutomated = automationPurchased;
            changed = true;
        }

        if (enabled)
        {
            if (Data.UnlockedNodeInstanceIds.Add(id))
                changed = true;
        }
        else
        {
            if (Data.UnlockedNodeInstanceIds.Remove(id))
                changed = true;
        }

        if (changed && requestSave)
            RequestSave();
    }

    public void SetUpgradeRank(string upgradeId, int rank, bool requestSave = true)
    {
        var id = NormalizeId(upgradeId);
        if (string.IsNullOrEmpty(id))
            return;

        EnsureDataInitialized();
        Data.Upgrades ??= new List<GameData.UpgradeStateData>();

        rank = Math.Max(0, rank);
        var entry = Data.Upgrades.Find(
            u => u != null && string.Equals(NormalizeId(u.Id), id, StringComparison.Ordinal)
        );

        bool changed = false;
        if (rank <= 0)
        {
            if (entry != null)
            {
                Data.Upgrades.Remove(entry);
                changed = true;
            }
        }
        else if (entry == null)
        {
            Data.Upgrades.Add(new GameData.UpgradeStateData { Id = id, PurchasedCount = rank });
            changed = true;
        }
        else if (entry.PurchasedCount != rank)
        {
            entry.PurchasedCount = rank;
            changed = true;
        }

        if (changed && requestSave)
            RequestSave();
    }

    public bool IsMilestoneFired(string milestoneId)
    {
        var id = NormalizeId(milestoneId);
        if (string.IsNullOrEmpty(id))
            return false;

        EnsureDataInitialized();
        return Data.FiredMilestoneIds.Contains(id);
    }

    public void MarkMilestoneFired(string milestoneId, bool requestSave = true)
    {
        var id = NormalizeId(milestoneId);
        if (string.IsNullOrEmpty(id))
            return;

        EnsureDataInitialized();
        if (!Data.FiredMilestoneIds.Add(id))
            return;

        if (requestSave)
            RequestSave();
    }

    public void SetNodeInstanceUnlocked(
        string nodeInstanceId,
        bool unlocked,
        bool requestSave = true
    )
    {
        var id = NormalizeId(nodeInstanceId);
        if (string.IsNullOrEmpty(id))
            return;

        EnsureDataInitialized();
        Data.UnlockedNodeInstanceIds ??= new HashSet<string>(StringComparer.Ordinal);
        Data.Generators ??= new List<GameData.GeneratorStateData>();

        bool changed = false;
        if (unlocked)
        {
            if (Data.UnlockedNodeInstanceIds.Add(id))
                changed = true;
        }
        else
        {
            if (Data.UnlockedNodeInstanceIds.Remove(id))
                changed = true;
        }

        var entry = Data.Generators.Find(
            g => g != null && string.Equals(NormalizeId(g.Id), id, StringComparison.Ordinal)
        );
        if (entry == null && unlocked)
        {
            entry = new GameData.GeneratorStateData
            {
                Id = id,
                IsOwned = false,
                IsEnabled = true,
                Level = 0,
                IsAutomationPurchased = false,
                IsAutomated = false,
            };
            Data.Generators.Add(entry);
            changed = true;
        }

        if (entry != null)
        {
            var expectedEnabled = unlocked || entry.IsOwned;
            if (entry.IsEnabled != expectedEnabled)
            {
                entry.IsEnabled = expectedEnabled;
                changed = true;
            }
        }

        if (changed && requestSave)
            RequestSave();
    }

    public void SetUnlockedNodeInstanceIds(IEnumerable<string> ids, bool requestSave = true)
    {
        EnsureDataInitialized();
        Data.UnlockedNodeInstanceIds ??= new HashSet<string>(StringComparer.Ordinal);
        Data.Generators ??= new List<GameData.GeneratorStateData>();

        var normalized = new HashSet<string>(StringComparer.Ordinal);
        if (ids != null)
        {
            foreach (var id in ids)
            {
                var key = NormalizeId(id);
                if (!string.IsNullOrEmpty(key))
                    normalized.Add(key);
            }
        }

        bool changed = false;
        if (!SetEquals(Data.UnlockedNodeInstanceIds, normalized))
        {
            Data.UnlockedNodeInstanceIds.Clear();
            foreach (var id in normalized)
                Data.UnlockedNodeInstanceIds.Add(id);
            changed = true;
        }

        for (int i = 0; i < Data.Generators.Count; i++)
        {
            var generator = Data.Generators[i];
            if (generator == null)
                continue;

            var id = NormalizeId(generator.Id);
            if (string.IsNullOrEmpty(id))
                continue;

            var expectedEnabled = normalized.Contains(id) || generator.IsOwned;
            if (generator.IsEnabled != expectedEnabled)
            {
                generator.IsEnabled = expectedEnabled;
                changed = true;
            }
        }

        foreach (var id in normalized)
        {
            var exists = Data.Generators.Exists(
                g => g != null && string.Equals(NormalizeId(g.Id), id, StringComparison.Ordinal)
            );
            if (exists)
                continue;

            Data.Generators.Add(
                new GameData.GeneratorStateData
                {
                    Id = id,
                    IsEnabled = true,
                    IsOwned = false,
                    Level = 0,
                    IsAutomationPurchased = false,
                    IsAutomated = false,
                }
            );
            changed = true;
        }

        if (changed && requestSave)
            RequestSave();
    }

    public bool ConsumePendingScopedResetReload()
    {
        var pending = hasPendingScopedResetReload;
        hasPendingScopedResetReload = false;
        return pending;
    }

    public void ApplyPrestigeResetScopes(
        GameDefinition definition,
        PrestigeDefinition prestige,
        ResourceCatalog resourceCatalog,
        bool requestSave = true
    )
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));
        if (prestige == null)
            throw new ArgumentNullException(nameof(prestige));
        if (resourceCatalog == null)
            throw new ArgumentNullException(nameof(resourceCatalog));
        if (prestige.resetScopes == null)
            throw new InvalidOperationException("SaveService: prestige.resetScopes is missing.");

        if (prestige.resetScopes.keepUnlocks != null && prestige.resetScopes.keepUnlocks.Length > 0)
        {
            throw new InvalidOperationException(
                "SaveService: prestige.resetScopes.keepUnlocks is not supported in this slice."
            );
        }

        if (
            prestige.resetScopes.keepUpgrades != null
            && prestige.resetScopes.keepUpgrades.Length > 0
        )
        {
            throw new InvalidOperationException(
                "SaveService: prestige.resetScopes.keepUpgrades is not supported in this slice."
            );
        }

        if (
            prestige.resetScopes.keepProjects != null
            && prestige.resetScopes.keepProjects.Length > 0
        )
        {
            throw new InvalidOperationException(
                "SaveService: prestige.resetScopes.keepProjects is not supported in this slice."
            );
        }

        EnsureDataInitialized();
        var defaults = CreateDefaultSaveData(definition);

        var resourceKindById = new Dictionary<string, string>(StringComparer.Ordinal);
        var resources = resourceCatalog.Resources;
        if (resources != null)
        {
            for (int i = 0; i < resources.Count; i++)
            {
                var resource = resources[i];
                var resourceId = NormalizeId(resource?.id);
                if (string.IsNullOrEmpty(resourceId))
                    continue;

                if (!resourceKindById.ContainsKey(resourceId))
                    resourceKindById[resourceId] = NormalizeId(resource?.kind);
            }
        }

        if (prestige.resetScopes.resetNodes)
        {
            Data.Generators.Clear();
            for (int i = 0; i < defaults.Generators.Count; i++)
            {
                var entry = defaults.Generators[i];
                if (entry == null)
                    continue;

                Data.Generators.Add(
                    new GameData.GeneratorStateData
                    {
                        Id = entry.Id,
                        IsOwned = entry.IsOwned,
                        IsEnabled = entry.IsEnabled,
                        Level = entry.Level,
                        IsAutomationPurchased = entry.IsAutomationPurchased,
                        IsAutomated = entry.IsAutomated,
                    }
                );
            }

            Data.UnlockedNodeInstanceIds.Clear();
            foreach (var id in defaults.UnlockedNodeInstanceIds)
                Data.UnlockedNodeInstanceIds.Add(id);
        }
        else
        {
            Data.UnlockedNodeInstanceIds.Clear();
        }

        Data.Upgrades.Clear();
        Data.FiredMilestoneIds.Clear();
        ClearActiveBuffState(requestSave: false);

        if (Data.Resources == null)
            Data.Resources = new List<GameData.ResourceBalanceData>();

        for (int i = 0; i < defaults.Resources.Count; i++)
        {
            var defaultEntry = defaults.Resources[i];
            var resourceId = NormalizeId(defaultEntry?.ResourceId);
            if (string.IsNullOrEmpty(resourceId))
                continue;

            var exists = Data.Resources.Exists(
                r => r != null && string.Equals(NormalizeId(r.ResourceId), resourceId, StringComparison.Ordinal)
            );
            if (exists)
                continue;

            Data.Resources.Add(
                new GameData.ResourceBalanceData { ResourceId = resourceId, Amount = 0d }
            );
        }

        for (int i = 0; i < Data.Resources.Count; i++)
        {
            var balance = Data.Resources[i];
            if (balance == null)
                continue;

            var resourceId = NormalizeId(balance.ResourceId);
            if (string.IsNullOrEmpty(resourceId))
                continue;

            var kind = resourceKindById.TryGetValue(resourceId, out var resolvedKind)
                ? resolvedKind
                : string.Empty;

            if (
                string.Equals(
                    resourceId,
                    NormalizeId(prestige.prestigeResource),
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            if (
                prestige.resetScopes.resetSoftCurrencies
                && string.Equals(kind, "softCurrency", StringComparison.OrdinalIgnoreCase)
            )
            {
                balance.Amount = 0d;
                continue;
            }

            if (
                !prestige.resetScopes.keepHardCurrencies
                && string.Equals(kind, "hardCurrency", StringComparison.OrdinalIgnoreCase)
            )
            {
                balance.Amount = 0d;
            }
        }

        if (Data.LifetimeEarnings == null)
            Data.LifetimeEarnings = new List<GameData.LifetimeEarningData>();

        for (int i = 0; i < defaults.LifetimeEarnings.Count; i++)
        {
            var defaultEntry = defaults.LifetimeEarnings[i];
            var resourceId = NormalizeId(defaultEntry?.ResourceId);
            if (string.IsNullOrEmpty(resourceId))
                continue;

            var exists = Data.LifetimeEarnings.Exists(
                r => r != null && string.Equals(NormalizeId(r.ResourceId), resourceId, StringComparison.Ordinal)
            );
            if (exists)
                continue;

            Data.LifetimeEarnings.Add(
                new GameData.LifetimeEarningData { ResourceId = resourceId, Amount = 0d }
            );
        }

        if (prestige.resetScopes.resetSoftCurrencies)
        {
            for (int i = 0; i < Data.LifetimeEarnings.Count; i++)
            {
                var entry = Data.LifetimeEarnings[i];
                if (entry == null)
                    continue;

                var resourceId = NormalizeId(entry.ResourceId);
                if (string.IsNullOrEmpty(resourceId))
                    continue;

                var kind = resourceKindById.TryGetValue(resourceId, out var resolvedKind)
                    ? resolvedKind
                    : string.Empty;

                if (string.Equals(kind, "softCurrency", StringComparison.OrdinalIgnoreCase))
                    entry.Amount = 0d;
            }
        }

        SortFactLists();
        hasPendingScopedResetReload = true;

        if (requestSave)
            SaveNow();
    }

    public GameData CreateDefaultSaveData(GameDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        var data = new GameData();
        data.EnsureInitialized();
        data.lastSeenUnixSeconds = GetCurrentUnixSeconds();
        data.Resources.Clear();
        data.LifetimeEarnings.Clear();
        data.Generators.Clear();
        data.Upgrades.Clear();
        data.ActiveBuffId = string.Empty;
        data.ActiveBuffExpiresAtUnixSeconds = 0;
        data.FiredMilestoneIds.Clear();
        data.UnlockedNodeInstanceIds.Clear();

        var seenResources = new HashSet<string>(StringComparer.Ordinal);
        if (definition.resources != null)
        {
            for (int i = 0; i < definition.resources.Count; i++)
            {
                var resourceId = NormalizeId(definition.resources[i]?.id);
                if (string.IsNullOrEmpty(resourceId) || !seenResources.Add(resourceId))
                    continue;

                data.Resources.Add(
                    new GameData.ResourceBalanceData { ResourceId = resourceId, Amount = 0d }
                );
                data.LifetimeEarnings.Add(
                    new GameData.LifetimeEarningData { ResourceId = resourceId, Amount = 0d }
                );
            }
        }

        var seenNodes = new HashSet<string>(StringComparer.Ordinal);
        if (definition.nodeInstances != null)
        {
            for (int i = 0; i < definition.nodeInstances.Count; i++)
            {
                var nodeInstance = definition.nodeInstances[i];
                var nodeInstanceId = NormalizeId(nodeInstance?.id);
                if (string.IsNullOrEmpty(nodeInstanceId) || !seenNodes.Add(nodeInstanceId))
                    continue;

                var enabled = nodeInstance?.initialState?.enabled ?? false;
                var owned = enabled;
                var level = Math.Max(0, nodeInstance?.initialState?.level ?? 0);

                if (owned && level < 1)
                    level = 1;
                if (!owned)
                    level = 0;

                data.Generators.Add(
                    new GameData.GeneratorStateData
                    {
                        Id = nodeInstanceId,
                        IsOwned = owned,
                        IsEnabled = enabled || owned,
                        Level = level,
                        IsAutomationPurchased = false,
                        IsAutomated = false,
                    }
                );

                if (enabled || owned)
                    data.UnlockedNodeInstanceIds.Add(nodeInstanceId);
            }
        }

        SortFactLists(data);
        data.EnsureInitialized();
        return data;
    }

    public void Dispose()
    {
        // Flush best-effort
        SaveNow();

        saveRequests.OnCompleted();
        saveRequests.Dispose();
        disposables.Dispose();
    }

    private GameData BuildFromDefaults(GameDefinition definition, GameData loaded, out bool changed)
    {
        var merged = CreateDefaultSaveData(definition);
        changed = loaded == null;

        if (loaded == null)
            return merged;

        loaded.EnsureInitialized();
        var now = GetCurrentUnixSeconds();
        var loadedLastSeen = loaded.lastSeenUnixSeconds;
        if (loadedLastSeen <= 0 || loadedLastSeen > now)
        {
            merged.lastSeenUnixSeconds = now;
            changed = true;
        }
        else
        {
            merged.lastSeenUnixSeconds = loadedLastSeen;
        }

        var validResourceIds = new HashSet<string>(StringComparer.Ordinal);
        if (definition.resources != null)
        {
            for (int i = 0; i < definition.resources.Count; i++)
            {
                var id = NormalizeId(definition.resources[i]?.id);
                if (!string.IsNullOrEmpty(id))
                    validResourceIds.Add(id);
            }
        }

        var validNodeInstanceIds = new HashSet<string>(StringComparer.Ordinal);
        if (definition.nodeInstances != null)
        {
            for (int i = 0; i < definition.nodeInstances.Count; i++)
            {
                var id = NormalizeId(definition.nodeInstances[i]?.id);
                if (!string.IsNullOrEmpty(id))
                    validNodeInstanceIds.Add(id);
            }
        }

        var validUpgradeIds = new HashSet<string>(StringComparer.Ordinal);
        if (definition.upgrades != null)
        {
            for (int i = 0; i < definition.upgrades.Count; i++)
            {
                var id = NormalizeId(definition.upgrades[i]?.id);
                if (!string.IsNullOrEmpty(id))
                    validUpgradeIds.Add(id);
            }
        }

        var validMilestoneIds = new HashSet<string>(StringComparer.Ordinal);
        if (definition.milestones != null)
        {
            for (int i = 0; i < definition.milestones.Count; i++)
            {
                var id = NormalizeId(definition.milestones[i]?.id);
                if (!string.IsNullOrEmpty(id))
                    validMilestoneIds.Add(id);
            }
        }

        var validBuffIds = new HashSet<string>(StringComparer.Ordinal);
        if (definition.buffs != null)
        {
            for (int i = 0; i < definition.buffs.Count; i++)
            {
                var id = NormalizeId(definition.buffs[i]?.id);
                if (!string.IsNullOrEmpty(id))
                    validBuffIds.Add(id);
            }
        }

        var resourceMap = new Dictionary<string, double>(StringComparer.Ordinal);
        if (loaded.Resources != null)
        {
            for (int i = 0; i < loaded.Resources.Count; i++)
            {
                var entry = loaded.Resources[i];
                var id = NormalizeId(entry?.ResourceId);
                if (string.IsNullOrEmpty(id))
                {
                    changed = true;
                    continue;
                }

                if (!validResourceIds.Contains(id))
                {
                    Debug.LogWarning($"SaveService: Save references missing resource id '{id}'. Skipping.");
                    changed = true;
                    continue;
                }

                if (!resourceMap.TryAdd(id, entry?.Amount ?? 0d))
                {
                    Debug.LogWarning($"SaveService: Duplicate saved resource id '{id}'. Using first value.");
                    changed = true;
                }
            }
        }

        for (int i = 0; i < merged.Resources.Count; i++)
        {
            var entry = merged.Resources[i];
            if (entry == null)
                continue;

            var id = NormalizeId(entry.ResourceId);
            if (resourceMap.TryGetValue(id, out var amount))
                entry.Amount = amount;
            else
                changed = true;
        }

        var lifetimeMap = new Dictionary<string, double>(StringComparer.Ordinal);
        if (loaded.LifetimeEarnings != null)
        {
            for (int i = 0; i < loaded.LifetimeEarnings.Count; i++)
            {
                var entry = loaded.LifetimeEarnings[i];
                var id = NormalizeId(entry?.ResourceId);
                if (string.IsNullOrEmpty(id))
                {
                    changed = true;
                    continue;
                }

                if (!validResourceIds.Contains(id))
                {
                    Debug.LogWarning(
                        $"SaveService: Save references missing lifetimeEarnings resource id '{id}'. Skipping."
                    );
                    changed = true;
                    continue;
                }

                var amount = Math.Max(0d, entry?.Amount ?? 0d);
                if (!lifetimeMap.TryAdd(id, amount))
                {
                    Debug.LogWarning(
                        $"SaveService: Duplicate lifetimeEarnings resource id '{id}'. Using first value."
                    );
                    changed = true;
                }
            }
        }

        for (int i = 0; i < merged.LifetimeEarnings.Count; i++)
        {
            var entry = merged.LifetimeEarnings[i];
            if (entry == null)
                continue;

            var id = NormalizeId(entry.ResourceId);
            if (lifetimeMap.TryGetValue(id, out var amount))
                entry.Amount = amount;
            else
                changed = true;
        }

        var savedNodeStates = new Dictionary<string, GameData.GeneratorStateData>(StringComparer.Ordinal);
        if (loaded.Generators != null)
        {
            for (int i = 0; i < loaded.Generators.Count; i++)
            {
                var entry = loaded.Generators[i];
                var id = NormalizeId(entry?.Id);
                if (string.IsNullOrEmpty(id))
                {
                    changed = true;
                    continue;
                }

                if (!validNodeInstanceIds.Contains(id))
                {
                    Debug.LogWarning(
                        $"SaveService: Save references missing nodeInstance id '{id}'. Skipping."
                    );
                    changed = true;
                    continue;
                }

                if (!savedNodeStates.TryAdd(id, entry))
                {
                    Debug.LogWarning(
                        $"SaveService: Duplicate saved nodeInstance id '{id}'. Using first value."
                    );
                    changed = true;
                }
            }
        }

        for (int i = 0; i < merged.Generators.Count; i++)
        {
            var entry = merged.Generators[i];
            if (entry == null)
                continue;

            var id = NormalizeId(entry.Id);
            if (!savedNodeStates.TryGetValue(id, out var saved))
            {
                changed = true;
                continue;
            }

            var automationPurchased = saved.IsAutomationPurchased || saved.IsAutomated;
            var owned = saved.IsOwned || automationPurchased;
            var enabled = saved.IsEnabled || owned;
            var level = Math.Max(0, saved.Level);

            if (owned && level < 1)
                level = 1;
            if (!owned)
                level = 0;

            entry.IsOwned = owned;
            entry.IsEnabled = enabled;
            entry.Level = level;
            entry.IsAutomationPurchased = automationPurchased;
            entry.IsAutomated = automationPurchased;
        }

        merged.Upgrades.Clear();
        var upgradeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (loaded.Upgrades != null)
        {
            for (int i = 0; i < loaded.Upgrades.Count; i++)
            {
                var entry = loaded.Upgrades[i];
                var id = NormalizeId(entry?.Id);
                if (string.IsNullOrEmpty(id))
                {
                    changed = true;
                    continue;
                }

                if (!validUpgradeIds.Contains(id))
                {
                    Debug.LogWarning($"SaveService: Save references missing upgrade id '{id}'. Skipping.");
                    changed = true;
                    continue;
                }

                var count = Math.Max(0, entry.PurchasedCount);
                if (count <= 0)
                {
                    if (entry.PurchasedCount < 0)
                        changed = true;
                    continue;
                }

                if (!upgradeCounts.TryAdd(id, count))
                {
                    Debug.LogWarning($"SaveService: Duplicate saved upgrade id '{id}'. Using first value.");
                    changed = true;
                }
            }
        }

        foreach (var kv in upgradeCounts)
        {
            merged.Upgrades.Add(
                new GameData.UpgradeStateData { Id = kv.Key, PurchasedCount = kv.Value }
            );
        }

        var loadedActiveBuffId = NormalizeId(loaded.ActiveBuffId);
        var loadedActiveBuffExpiresAt = Math.Max(0, loaded.ActiveBuffExpiresAtUnixSeconds);
        if (!string.IsNullOrEmpty(loadedActiveBuffId))
        {
            if (!validBuffIds.Contains(loadedActiveBuffId))
            {
                Debug.LogWarning(
                    $"SaveService: Save references missing buff id '{loadedActiveBuffId}'. Clearing active buff."
                );
                changed = true;
            }
            else if (loadedActiveBuffExpiresAt <= now)
            {
                changed = true;
            }
            else
            {
                merged.ActiveBuffId = loadedActiveBuffId;
                merged.ActiveBuffExpiresAtUnixSeconds = loadedActiveBuffExpiresAt;
            }
        }
        else if (loadedActiveBuffExpiresAt > 0)
        {
            changed = true;
        }

        merged.FiredMilestoneIds.Clear();
        if (loaded.FiredMilestoneIds != null)
        {
            foreach (var milestoneId in loaded.FiredMilestoneIds)
            {
                var id = NormalizeId(milestoneId);
                if (string.IsNullOrEmpty(id))
                {
                    changed = true;
                    continue;
                }

                if (!validMilestoneIds.Contains(id))
                {
                    Debug.LogWarning(
                        $"SaveService: Save references missing milestone id '{id}'. Skipping."
                    );
                    changed = true;
                    continue;
                }

                merged.FiredMilestoneIds.Add(id);
            }
        }

        if (loaded.UnlockedNodeInstanceIds != null)
        {
            foreach (var nodeInstanceId in loaded.UnlockedNodeInstanceIds)
            {
                var id = NormalizeId(nodeInstanceId);
                if (string.IsNullOrEmpty(id))
                {
                    changed = true;
                    continue;
                }

                if (!validNodeInstanceIds.Contains(id))
                {
                    Debug.LogWarning(
                        $"SaveService: Save references missing unlocked nodeInstance id '{id}'. Skipping."
                    );
                    changed = true;
                    continue;
                }

                merged.UnlockedNodeInstanceIds.Add(id);
            }
        }

        for (int i = 0; i < merged.Generators.Count; i++)
        {
            var generator = merged.Generators[i];
            if (generator == null)
                continue;

            var id = NormalizeId(generator.Id);
            if (string.IsNullOrEmpty(id))
                continue;

            generator.IsEnabled = generator.IsOwned || merged.UnlockedNodeInstanceIds.Contains(id);
        }

        SortFactLists(merged);
        merged.EnsureInitialized();
        return merged;
    }

    private void EnsureDataInitialized()
    {
        Data ??= new GameData();
        Data.EnsureInitialized();
    }

    private void SortFactLists()
    {
        SortFactLists(Data);
    }

    private static void SortFactLists(GameData data)
    {
        if (data == null)
            return;

        data.Resources?.Sort(
            (a, b) =>
                string.Compare(
                    NormalizeId(a?.ResourceId),
                    NormalizeId(b?.ResourceId),
                    StringComparison.Ordinal
                )
        );
        data.LifetimeEarnings?.Sort(
            (a, b) =>
                string.Compare(
                    NormalizeId(a?.ResourceId),
                    NormalizeId(b?.ResourceId),
                    StringComparison.Ordinal
                )
        );
        data.Generators?.Sort(
            (a, b) =>
                string.Compare(NormalizeId(a?.Id), NormalizeId(b?.Id), StringComparison.Ordinal)
        );
        data.Upgrades?.Sort(
            (a, b) =>
                string.Compare(NormalizeId(a?.Id), NormalizeId(b?.Id), StringComparison.Ordinal)
        );
    }

    private static bool SetEquals(HashSet<string> a, HashSet<string> b)
    {
        if (a == null || b == null)
            return a == b;

        return a.SetEquals(b);
    }

    private static long GetCurrentUnixSeconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private static string NormalizeId(string id)
    {
        return (id ?? string.Empty).Trim();
    }
}
