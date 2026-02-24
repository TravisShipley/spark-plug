using System;
using System.Collections.Generic;
using UniRx;
using UnityEngine;

/*
Manual test path (vertical slice):
1) Start with a fresh save.
2) Level apple to milestone 25.
3) Observe milestone fire log.
4) Observe trigger match + reward pool roll logs.
5) Confirm currencyHard increased by reward amount.
6) Reload and confirm no duplicate grant for already-fired milestone.
*/
public sealed class TriggerService : IDisposable
{
    private const string MilestoneFiredEventType = "milestone.fired";

    private readonly Dictionary<string, List<TriggerDefinition>> triggersByEventType = new(
        StringComparer.Ordinal
    );
    private readonly RewardService rewardService;
    private readonly GameEventStream gameEventStream;
    private readonly CompositeDisposable disposables = new();

    public TriggerService(
        GameDefinitionService gameDefinitionService,
        WalletService walletService,
        GameEventStream gameEventStream
    )
    {
        if (gameDefinitionService == null)
            throw new ArgumentNullException(nameof(gameDefinitionService));
        if (walletService == null)
            throw new ArgumentNullException(nameof(walletService));
        this.gameEventStream =
            gameEventStream ?? throw new ArgumentNullException(nameof(gameEventStream));

        rewardService = new RewardService(gameDefinitionService, walletService);
        IndexTriggers(gameDefinitionService.Triggers);

        this.gameEventStream.MilestoneFired.Subscribe(OnMilestoneFired).AddTo(disposables);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }

    private void IndexTriggers(IReadOnlyList<TriggerDefinition> triggers)
    {
        if (triggers == null || triggers.Count == 0)
            return;

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < triggers.Count; i++)
        {
            var trigger = triggers[i];
            if (trigger == null)
                continue;

            var triggerId = NormalizeId(trigger.id);
            if (string.IsNullOrEmpty(triggerId))
                throw new InvalidOperationException($"triggers[{i}].id is empty.");
            if (!seenIds.Add(triggerId))
                throw new InvalidOperationException($"Duplicate trigger id '{triggerId}'.");

            var eventType = ResolveTriggerEventType(trigger);
            if (!string.Equals(eventType, MilestoneFiredEventType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Trigger '{triggerId}' uses unsupported event '{eventType}'. "
                        + $"Only '{MilestoneFiredEventType}' is supported in this slice."
                );
            }

            if (!triggersByEventType.TryGetValue(eventType, out var list))
            {
                list = new List<TriggerDefinition>();
                triggersByEventType[eventType] = list;
            }

            list.Add(trigger);
        }
    }

    private void OnMilestoneFired(GameEventStream.MilestoneFiredEvent evt)
    {
        if (!triggersByEventType.TryGetValue(MilestoneFiredEventType, out var triggers))
            return;

        for (int i = 0; i < triggers.Count; i++)
        {
            var trigger = triggers[i];
            if (trigger == null)
                continue;

            if (!EvaluateConditions(trigger, evt))
                continue;

            var triggerId = NormalizeId(trigger.id);
            Debug.Log($"[Trigger] Executing '{triggerId}' for milestone '{evt.milestoneId}'.");
            ExecuteActions(trigger);
        }
    }

    private bool EvaluateConditions(
        TriggerDefinition trigger,
        GameEventStream.MilestoneFiredEvent evt
    )
    {
        var conditions = trigger.conditions;
        if (conditions == null || conditions.Length == 0)
            return true;

        for (int i = 0; i < conditions.Length; i++)
        {
            var condition = conditions[i];
            if (condition == null)
                continue;

            var type = NormalizeId(condition.type);
            if (string.IsNullOrEmpty(type))
                throw new InvalidOperationException(
                    $"Trigger '{trigger.id}' has condition with empty type."
                );

            if (string.Equals(type, "milestoneIdEquals", StringComparison.Ordinal))
            {
                var expectedMilestoneId = NormalizeId(condition.args?.milestoneId);
                if (string.IsNullOrEmpty(expectedMilestoneId))
                {
                    throw new InvalidOperationException(
                        $"Trigger '{trigger.id}' condition '{type}' requires args.milestoneId."
                    );
                }

                if (!string.Equals(expectedMilestoneId, evt.milestoneId, StringComparison.Ordinal))
                    return false;
                continue;
            }

            throw new InvalidOperationException(
                $"Trigger '{trigger.id}' has unsupported condition type '{type}'. "
                    + "Only 'milestoneIdEquals' is supported in this slice."
            );
        }

        return true;
    }

    private void ExecuteActions(TriggerDefinition trigger)
    {
        var actions = trigger.actions;
        if (actions == null || actions.Length == 0)
            return;

        for (int i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            if (action == null)
                continue;

            var actionType = NormalizeId(action.type);
            if (string.IsNullOrEmpty(actionType))
                throw new InvalidOperationException(
                    $"Trigger '{trigger.id}' has action with empty type."
                );

            if (string.Equals(actionType, "rollRewardPool", StringComparison.Ordinal))
            {
                rewardService.Roll(action.rewardPoolId);
                continue;
            }

            throw new InvalidOperationException(
                $"Trigger '{trigger.id}' has unsupported action '{actionType}'. "
                    + "Only 'rollRewardPool' is supported in this slice."
            );
        }
    }

    private static string ResolveTriggerEventType(TriggerDefinition trigger)
    {
        var normalizedEventType = NormalizeId(trigger.eventType);
        if (!string.IsNullOrEmpty(normalizedEventType))
            return normalizedEventType;

        var normalizedEvent = NormalizeId(trigger.@event);
        if (!string.IsNullOrEmpty(normalizedEvent))
            return normalizedEvent;

        throw new InvalidOperationException($"Trigger '{trigger.id}' is missing event type.");
    }

    private static string NormalizeId(string raw)
    {
        return (raw ?? string.Empty).Trim();
    }
}
