using System;
using System.Collections.Generic;
using UniRx;
using UnityEngine;

public sealed class MilestoneService : IDisposable
{
    private readonly GameDefinitionService gameDefinitionService;
    private readonly SaveService saveService;
    private readonly ModifierService modifierService;

    private readonly Dictionary<string, string> nodeIdByGeneratorId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<MilestoneDefinition>> milestonesByNodeId = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, ModifierDefinition> modifiersById = new(StringComparer.Ordinal);
    private readonly HashSet<string> warnedMilestones = new(StringComparer.Ordinal);
    private readonly CompositeDisposable disposables = new();

    public MilestoneService(
        GameDefinitionService gameDefinitionService,
        IReadOnlyList<GeneratorService> generators,
        SaveService saveService,
        ModifierService modifierService
    )
    {
        this.gameDefinitionService =
            gameDefinitionService ?? throw new ArgumentNullException(nameof(gameDefinitionService));
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
        this.modifierService =
            modifierService ?? throw new ArgumentNullException(nameof(modifierService));

        IndexMilestones(gameDefinitionService.Milestones);
        IndexModifiers(gameDefinitionService.Modifiers);
        SubscribeGenerators(generators);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }

    private void SubscribeGenerators(IReadOnlyList<GeneratorService> generators)
    {
        if (generators == null || generators.Count == 0)
            return;

        for (int i = 0; i < generators.Count; i++)
        {
            var generator = generators[i];
            if (generator == null)
                continue;

            var generatorId = (generator.Id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(generatorId))
                continue;

            if (!TryResolveNodeIdForGenerator(generatorId, out var nodeId))
                continue;

            if (!milestonesByNodeId.ContainsKey(nodeId))
                continue;

            generator
                .Level.DistinctUntilChanged()
                .Subscribe(level => EvaluateMilestonesForNode(nodeId, level))
                .AddTo(disposables);

            EvaluateMilestonesForNode(nodeId, generator.Level.Value);
        }
    }

    private bool TryResolveNodeIdForGenerator(string generatorId, out string nodeId)
    {
        nodeId = null;
        if (nodeIdByGeneratorId.TryGetValue(generatorId, out var cached))
        {
            nodeId = cached;
            return !string.IsNullOrEmpty(nodeId);
        }

        if (!gameDefinitionService.TryGetNodeInstance(generatorId, out var nodeInstance))
            return false;

        var resolved = (nodeInstance?.nodeId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(resolved))
            return false;

        nodeIdByGeneratorId[generatorId] = resolved;
        nodeId = resolved;
        return true;
    }

    private void EvaluateMilestonesForNode(string nodeId, int currentLevel)
    {
        if (string.IsNullOrEmpty(nodeId))
            return;

        if (
            !milestonesByNodeId.TryGetValue(nodeId, out var nodeMilestones)
            || nodeMilestones == null
        )
            return;

        bool firedAny = false;
        for (int i = 0; i < nodeMilestones.Count; i++)
        {
            var milestone = nodeMilestones[i];
            if (milestone == null)
                continue;

            var milestoneId = (milestone.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(milestoneId))
                continue;

            if (saveService.IsMilestoneFired(milestoneId))
                continue;

            if (currentLevel < milestone.atLevel)
                continue;

            if (!ValidateMilestone(milestone))
                continue;

            saveService.MarkMilestoneFired(milestoneId, requestSave: false);
            EventSystem.OnMilestoneFired.OnNext(
                new EventSystem.MilestoneFiredEvent(
                    milestoneId,
                    (milestone.nodeId ?? string.Empty).Trim(),
                    (milestone.zoneId ?? string.Empty).Trim(),
                    milestone.atLevel
                )
            );
            var modifierIds = new List<string>();
            for (int e = 0; e < milestone.grantEffects.Length; e++)
            {
                var modifierId = (milestone.grantEffects[e]?.modifierId ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(modifierId))
                    modifierIds.Add(modifierId);
            }

            Debug.Log(
                $"[Milestone] Fired '{milestoneId}' at level {currentLevel}. Applying modifier(s): {string.Join(", ", modifierIds)}"
            );
            firedAny = true;
        }

        if (firedAny)
        {
            saveService.RequestSave();
            modifierService.RebuildActiveModifiers($"milestone:{nodeId}:{currentLevel}");
        }
    }

    private bool ValidateMilestone(MilestoneDefinition milestone)
    {
        var milestoneId = (milestone?.id ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(milestoneId))
            return false;

        var nodeId = (milestone.nodeId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(nodeId) || !gameDefinitionService.TryGetNode(nodeId, out _))
        {
            LogMilestoneErrorOnce(milestoneId);
            return false;
        }

        if (milestone.grantEffects == null || milestone.grantEffects.Length == 0)
        {
            LogMilestoneErrorOnce(milestoneId);
            return false;
        }

        for (int i = 0; i < milestone.grantEffects.Length; i++)
        {
            var modifierId = (milestone.grantEffects[i]?.modifierId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(modifierId) || !modifiersById.ContainsKey(modifierId))
            {
                LogMilestoneErrorOnce(milestoneId);
                return false;
            }
        }

        return true;
    }

    private void IndexMilestones(IReadOnlyList<MilestoneDefinition> milestones)
    {
        if (milestones == null)
            return;

        for (int i = 0; i < milestones.Count; i++)
        {
            var milestone = milestones[i];
            if (milestone == null)
                continue;

            var milestoneId = (milestone.id ?? string.Empty).Trim();
            var nodeId = (milestone.nodeId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(milestoneId))
                continue;
            if (string.IsNullOrEmpty(nodeId))
            {
                LogMilestoneErrorOnce(milestoneId);
                continue;
            }

            if (!gameDefinitionService.TryGetNode(nodeId, out _))
            {
                LogMilestoneErrorOnce(milestoneId);
                continue;
            }

            if (!milestonesByNodeId.TryGetValue(nodeId, out var list) || list == null)
            {
                list = new List<MilestoneDefinition>();
                milestonesByNodeId[nodeId] = list;
            }

            list.Add(milestone);
        }

        foreach (var kv in milestonesByNodeId)
        {
            kv.Value.Sort(
                (a, b) =>
                    string.Compare(
                        (a?.id ?? string.Empty).Trim(),
                        (b?.id ?? string.Empty).Trim(),
                        StringComparison.Ordinal
                    )
            );
        }
    }

    private void IndexModifiers(IReadOnlyList<ModifierDefinition> modifiers)
    {
        if (modifiers == null)
            return;

        for (int i = 0; i < modifiers.Count; i++)
        {
            var modifier = modifiers[i];
            if (modifier == null)
                continue;

            var modifierId = (modifier.id ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(modifierId))
                continue;
            if (modifiersById.ContainsKey(modifierId))
                continue;

            modifiersById[modifierId] = modifier;
        }
    }

    private void LogMilestoneErrorOnce(string milestoneId)
    {
        if (!warnedMilestones.Add(milestoneId))
            return;

        Debug.LogError($"Milestone '{milestoneId}' references missing node or modifier");
    }
}
