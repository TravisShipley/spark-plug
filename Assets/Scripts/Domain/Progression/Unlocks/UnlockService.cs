using System;
using System.Collections.Generic;
using System.Linq;
using UniRx;
using UnityEngine;

public enum UnlockRequirementType
{
    NodeOwned,
    NodeLevelAtLeast,
    UpgradePurchased,
}

public sealed class UnlockService : IDisposable
{
    private readonly GameDefinitionService gameDefinitionService;
    private readonly IGeneratorLookup generatorLookup;
    private readonly UpgradeService upgradeService;
    private readonly SaveService saveService;

    private readonly HashSet<string> unlockedIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReactiveProperty<bool>> unlockedById = new(
        StringComparer.Ordinal
    );
    private readonly HashSet<string> warningKeys = new(StringComparer.Ordinal);

    private readonly Subject<string> unlockedSubject = new();
    private readonly SerialDisposable zoneSubscriptions = new();

    public UnlockService(
        GameDefinitionService gameDefinitionService,
        IGeneratorLookup generatorLookup,
        UpgradeService upgradeService,
        SaveService saveService
    )
    {
        this.gameDefinitionService =
            gameDefinitionService ?? throw new ArgumentNullException(nameof(gameDefinitionService));
        this.generatorLookup =
            generatorLookup ?? throw new ArgumentNullException(nameof(generatorLookup));
        this.upgradeService = upgradeService ?? throw new ArgumentNullException(nameof(upgradeService));
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
    }

    public IObservable<string> OnUnlocked => unlockedSubject;

    public bool IsUnlocked(string nodeInstanceId)
    {
        var id = NormalizeId(nodeInstanceId);
        return !string.IsNullOrEmpty(id) && unlockedIds.Contains(id);
    }

    public IObservable<bool> ObserveIsUnlocked(string nodeInstanceId)
    {
        var id = NormalizeId(nodeInstanceId);
        if (string.IsNullOrEmpty(id))
            return Observable.Return(false);

        return EnsureUnlockProperty(id).DistinctUntilChanged();
    }

    public void LoadUnlockedIds(IEnumerable<string> ids)
    {
        unlockedIds.Clear();

        if (ids != null)
        {
            foreach (var id in ids)
            {
                var normalizedId = NormalizeId(id);
                if (!string.IsNullOrEmpty(normalizedId))
                    unlockedIds.Add(normalizedId);
            }
        }

        foreach (var kv in unlockedById)
            kv.Value.Value = unlockedIds.Contains(kv.Key);

        SyncUnlockedIdsToSave(requestSave: false);
    }

    public IReadOnlyCollection<string> GetUnlockedIds()
    {
        var snapshot = new List<string>(unlockedIds);
        snapshot.Sort(StringComparer.Ordinal);
        return snapshot;
    }

    public void InitializeForZone(string zoneId)
    {
        var normalizedZoneId = NormalizeId(zoneId);
        var zoneEntries = GetUnlockEntriesForZone(normalizedZoneId);
        var targetIds = CollectTargetIds(zoneEntries);

        var nodeInstances = gameDefinitionService.NodeInstanceCatalog.GetForZone(normalizedZoneId);
        for (int i = 0; i < nodeInstances.Count; i++)
        {
            var nodeInstance = nodeInstances[i];
            if (nodeInstance == null)
                continue;

            var nodeInstanceId = NormalizeId(nodeInstance.id);
            if (string.IsNullOrEmpty(nodeInstanceId))
                continue;

            bool isGraphTarget = targetIds.Contains(nodeInstanceId);
            bool initiallyEnabled = nodeInstance.initialState?.enabled ?? false;

            if (!isGraphTarget || initiallyEnabled || unlockedIds.Contains(nodeInstanceId))
                UnlockInternal(nodeInstanceId, emitEvent: false, markSaveDirty: false);
            else
                EnsureUnlockProperty(nodeInstanceId).Value = false;
        }

        var subscriptions = new CompositeDisposable();
        zoneSubscriptions.Disposable = subscriptions;

        zoneEntries.Sort((a, b) =>
            string.Compare(
                ResolveTargetNodeInstanceId(a),
                ResolveTargetNodeInstanceId(b),
                StringComparison.Ordinal
            )
        );

        for (int i = 0; i < zoneEntries.Count; i++)
        {
            var entry = zoneEntries[i];
            var targetNodeInstanceId = ResolveTargetNodeInstanceId(entry);
            if (string.IsNullOrEmpty(targetNodeInstanceId))
                continue;

            var requirementsObservable = BuildRequirementsObservable(entry);
            requirementsObservable
                .Where(passes => passes)
                .Subscribe(_ => UnlockInternal(targetNodeInstanceId, emitEvent: true, markSaveDirty: true))
                .AddTo(subscriptions);
        }

        SyncUnlockedIdsToSave(requestSave: false);
    }

    public void Dispose()
    {
        zoneSubscriptions.Dispose();

        unlockedSubject.OnCompleted();
        unlockedSubject.Dispose();

        foreach (var kv in unlockedById)
            kv.Value?.Dispose();

        unlockedById.Clear();
    }

    private IObservable<bool> BuildRequirementsObservable(UnlockGraphDefinition entry)
    {
        var requirements = entry?.requirements;
        if (requirements == null || requirements.Length == 0)
            return Observable.Return(true);

        var requirementObservables = new List<IObservable<bool>>(requirements.Length);
        for (int i = 0; i < requirements.Length; i++)
            requirementObservables.Add(ObserveRequirement(entry, i, requirements[i]));

        return Observable
            .CombineLatest(requirementObservables.ToArray())
            .Select(values =>
            {
                for (int i = 0; i < values.Count; i++)
                {
                    if (!values[i])
                        return false;
                }

                return true;
            })
            .DistinctUntilChanged();
    }

    private IObservable<bool> ObserveRequirement(
        UnlockGraphDefinition entry,
        int requirementIndex,
        UnlockRequirement requirement
    )
    {
        if (requirement == null)
        {
            LogWarningOnce(
                $"entry:{entry?.id}:req:{requirementIndex}:null",
                $"UnlockService: unlock entry '{entry?.id}' has a null requirement at index {requirementIndex}."
            );
            return Observable.Return(false);
        }

        if (!TryParseRequirementType(requirement.type, out var requirementType))
        {
            LogWarningOnce(
                $"entry:{entry?.id}:req:{requirementIndex}:type:{requirement.type}",
                $"UnlockService: unlock entry '{entry?.id}' has unsupported requirement type '{requirement.type}'."
            );
            return Observable.Return(false);
        }

        switch (requirementType)
        {
            case UnlockRequirementType.NodeOwned:
            {
                var nodeInstanceId = ResolveRequirementNodeInstanceId(requirement);
                if (string.IsNullOrEmpty(nodeInstanceId))
                {
                    LogWarningOnce(
                        $"entry:{entry?.id}:req:{requirementIndex}:missing_node_owned",
                        $"UnlockService: unlock entry '{entry?.id}' has NodeOwned requirement with missing nodeInstanceId."
                    );
                    return Observable.Return(false);
                }

                if (!generatorLookup.TryGetGenerator(nodeInstanceId, out var generator) || generator == null)
                {
                    LogWarningOnce(
                        $"entry:{entry?.id}:req:{requirementIndex}:missing_generator:{nodeInstanceId}",
                        $"UnlockService: unlock entry '{entry?.id}' references missing generator '{nodeInstanceId}'."
                    );
                    return Observable.Return(false);
                }

                return generator.IsOwned.DistinctUntilChanged();
            }

            case UnlockRequirementType.NodeLevelAtLeast:
            {
                var nodeInstanceId = ResolveRequirementNodeInstanceId(requirement);
                var minLevel = ResolveRequirementMinLevel(requirement);
                if (string.IsNullOrEmpty(nodeInstanceId) || minLevel <= 0)
                {
                    LogWarningOnce(
                        $"entry:{entry?.id}:req:{requirementIndex}:invalid_level_req",
                        $"UnlockService: unlock entry '{entry?.id}' has invalid NodeLevelAtLeast requirement (nodeInstanceId='{nodeInstanceId}', minLevel={minLevel})."
                    );
                    return Observable.Return(false);
                }

                if (!generatorLookup.TryGetGenerator(nodeInstanceId, out var generator) || generator == null)
                {
                    LogWarningOnce(
                        $"entry:{entry?.id}:req:{requirementIndex}:missing_generator:{nodeInstanceId}",
                        $"UnlockService: unlock entry '{entry?.id}' references missing generator '{nodeInstanceId}'."
                    );
                    return Observable.Return(false);
                }

                return generator
                    .Level.Select(level => level >= minLevel)
                    .DistinctUntilChanged();
            }

            case UnlockRequirementType.UpgradePurchased:
            {
                var upgradeId = ResolveRequirementUpgradeId(requirement);
                if (string.IsNullOrEmpty(upgradeId))
                {
                    LogWarningOnce(
                        $"entry:{entry?.id}:req:{requirementIndex}:missing_upgrade",
                        $"UnlockService: unlock entry '{entry?.id}' has UpgradePurchased requirement with missing upgradeId."
                    );
                    return Observable.Return(false);
                }

                if (!gameDefinitionService.TryGetUpgrade(upgradeId, out _))
                {
                    LogWarningOnce(
                        $"entry:{entry?.id}:req:{requirementIndex}:missing_upgrade:{upgradeId}",
                        $"UnlockService: unlock entry '{entry?.id}' references missing upgrade '{upgradeId}'."
                    );
                    return Observable.Return(false);
                }

                return upgradeService
                    .PurchasedCount(upgradeId)
                    .Select(purchasedCount => purchasedCount > 0)
                    .DistinctUntilChanged();
            }

            default:
                return Observable.Return(false);
        }
    }

    private bool UnlockInternal(string nodeInstanceId, bool emitEvent, bool markSaveDirty)
    {
        var id = NormalizeId(nodeInstanceId);
        if (string.IsNullOrEmpty(id))
            return false;

        if (!unlockedIds.Add(id))
        {
            EnsureUnlockProperty(id).Value = true;
            return false;
        }

        EnsureUnlockProperty(id).Value = true;

        if (markSaveDirty)
            saveService.SetNodeInstanceUnlocked(id, unlocked: true, requestSave: true);

        if (emitEvent)
            unlockedSubject.OnNext(id);

        return true;
    }

    private void SyncUnlockedIdsToSave(bool requestSave)
    {
        saveService.SetUnlockedNodeInstanceIds(unlockedIds, requestSave: false);
        if (requestSave)
            saveService.RequestSave();
    }

    private ReactiveProperty<bool> EnsureUnlockProperty(string nodeInstanceId)
    {
        if (!unlockedById.TryGetValue(nodeInstanceId, out var unlockProperty) || unlockProperty == null)
        {
            unlockProperty = new ReactiveProperty<bool>(unlockedIds.Contains(nodeInstanceId));
            unlockedById[nodeInstanceId] = unlockProperty;
        }

        return unlockProperty;
    }

    private List<UnlockGraphDefinition> GetUnlockEntriesForZone(string zoneId)
    {
        var allEntries = gameDefinitionService.UnlockGraph;
        var zoneEntries = new List<UnlockGraphDefinition>();

        if (allEntries == null || allEntries.Count == 0)
            return zoneEntries;

        for (int i = 0; i < allEntries.Count; i++)
        {
            var entry = allEntries[i];
            if (entry == null)
                continue;

            var entryZoneId = NormalizeId(entry.zoneId);
            if (!string.IsNullOrEmpty(zoneId) && !string.IsNullOrEmpty(entryZoneId))
            {
                if (!string.Equals(zoneId, entryZoneId, StringComparison.Ordinal))
                    continue;
            }

            var targetNodeInstanceId = ResolveTargetNodeInstanceId(entry);
            if (string.IsNullOrEmpty(targetNodeInstanceId))
                continue;

            if (!gameDefinitionService.TryGetNodeInstance(targetNodeInstanceId, out _))
            {
                LogWarningOnce(
                    $"entry:{entry.id}:missing_target:{targetNodeInstanceId}",
                    $"UnlockService: unlock entry '{entry.id}' references missing target node instance '{targetNodeInstanceId}'."
                );
                continue;
            }

            zoneEntries.Add(entry);
        }

        return zoneEntries;
    }

    private static HashSet<string> CollectTargetIds(IReadOnlyList<UnlockGraphDefinition> entries)
    {
        var targetIds = new HashSet<string>(StringComparer.Ordinal);
        if (entries == null)
            return targetIds;

        for (int i = 0; i < entries.Count; i++)
        {
            var targetNodeInstanceId = ResolveTargetNodeInstanceId(entries[i]);
            if (!string.IsNullOrEmpty(targetNodeInstanceId))
                targetIds.Add(targetNodeInstanceId);
        }

        return targetIds;
    }

    private static bool TryParseRequirementType(string rawType, out UnlockRequirementType type)
    {
        type = UnlockRequirementType.NodeOwned;
        var normalized = (rawType ?? string.Empty).Trim();

        if (string.Equals(normalized, "NodeOwned", StringComparison.OrdinalIgnoreCase))
        {
            type = UnlockRequirementType.NodeOwned;
            return true;
        }

        if (string.Equals(normalized, "NodeLevelAtLeast", StringComparison.OrdinalIgnoreCase))
        {
            type = UnlockRequirementType.NodeLevelAtLeast;
            return true;
        }

        if (string.Equals(normalized, "UpgradePurchased", StringComparison.OrdinalIgnoreCase))
        {
            type = UnlockRequirementType.UpgradePurchased;
            return true;
        }

        return false;
    }

    private static string ResolveTargetNodeInstanceId(UnlockGraphDefinition entry)
    {
        var direct = NormalizeId(entry?.targetNodeInstanceId);
        if (!string.IsNullOrEmpty(direct))
            return direct;

        var unlocks = entry?.unlocks;
        if (unlocks == null || unlocks.Length == 0)
            return string.Empty;

        for (int i = 0; i < unlocks.Length; i++)
        {
            var unlock = unlocks[i];
            if (unlock == null)
                continue;

            var kind = NormalizeId(unlock.kind);
            if (!string.Equals(kind, "nodeInstance", StringComparison.OrdinalIgnoreCase))
                continue;

            var unlockId = NormalizeId(unlock.id);
            if (!string.IsNullOrEmpty(unlockId))
                return unlockId;
        }

        return string.Empty;
    }

    private static string ResolveRequirementNodeInstanceId(UnlockRequirement requirement)
    {
        var direct = NormalizeId(requirement?.nodeInstanceId);
        if (!string.IsNullOrEmpty(direct))
            return direct;

        var fromArgs = NormalizeId(requirement?.args?.nodeInstanceId);
        if (!string.IsNullOrEmpty(fromArgs))
            return fromArgs;

        return NormalizeId(requirement?.args?.id);
    }

    private static int ResolveRequirementMinLevel(UnlockRequirement requirement)
    {
        if (requirement == null)
            return 0;

        if (requirement.minLevel > 0)
            return requirement.minLevel;

        if (requirement.args != null)
        {
            if (requirement.args.minLevel > 0)
                return requirement.args.minLevel;

            if (requirement.args.level > 0)
                return requirement.args.level;
        }

        return 0;
    }

    private static string ResolveRequirementUpgradeId(UnlockRequirement requirement)
    {
        var direct = NormalizeId(requirement?.upgradeId);
        if (!string.IsNullOrEmpty(direct))
            return direct;

        var fromArgs = NormalizeId(requirement?.args?.upgradeId);
        if (!string.IsNullOrEmpty(fromArgs))
            return fromArgs;

        return NormalizeId(requirement?.args?.id);
    }

    private static string NormalizeId(string raw)
    {
        return (raw ?? string.Empty).Trim();
    }

    private void LogWarningOnce(string key, string message)
    {
        if (!warningKeys.Add(key))
            return;

        Debug.LogWarning(message);
    }
}
