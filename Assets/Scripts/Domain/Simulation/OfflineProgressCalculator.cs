using System;
using System.Collections.Generic;
using UnityEngine;

/*
Manual generator persistence checklist:
1) Leave a manual generator ready to collect, reload, and verify it is still waiting to collect.
2) Leave a manual generator mid-cycle, reload, and verify the remaining time resumes correctly.
3) Leave two manual generators with 30m and 1h 5m remaining, return after 1h, and verify:
   first is waiting to collect, second has 5m remaining.
4) Verify automated generators still grant offline gains and resume with the correct partial-cycle remainder.
5) Collect a waiting payout, reload, and verify the pending state is cleared.
*/
public sealed class OfflineProgressCalculator
{
    private const double MinCycleDurationSeconds = 0.0001d;
    private const double MaxPassiveOfflineSeconds = 8d * 60d * 60d;

    private readonly GameDefinitionService gameDefinitionService;
    private readonly ModifierService modifierService;

    public OfflineProgressCalculator(
        GameDefinitionService gameDefinitionService,
        ModifierService modifierService
    )
    {
        this.gameDefinitionService =
            gameDefinitionService ?? throw new ArgumentNullException(nameof(gameDefinitionService));
        this.modifierService = modifierService;
    }

    public OfflineSessionResult Calculate(long secondsAway, GameData saveData)
    {
        return Calculate((double)secondsAway, saveData, respectOfflineCap: true);
    }

    public OfflineSessionResult Calculate(
        double elapsedSeconds,
        GameData saveData,
        bool respectOfflineCap = true
    )
    {
        var sanitizedElapsedSeconds = SanitizeElapsedSeconds(elapsedSeconds);
        var appliedElapsedSeconds = respectOfflineCap
            ? Math.Min(sanitizedElapsedSeconds, MaxPassiveOfflineSeconds)
            : sanitizedElapsedSeconds;

        var result = new OfflineSessionResult
        {
            secondsAway = (long)Math.Floor(appliedElapsedSeconds),
        };

        if (appliedElapsedSeconds < MinCycleDurationSeconds || saveData == null)
            return result;

        var generatorsById = BuildGeneratorStateLookup(saveData.Generators);
        var nodeInstances = gameDefinitionService.NodeInstances;
        if (nodeInstances == null || nodeInstances.Count == 0)
            return result;

        for (int i = 0; i < nodeInstances.Count; i++)
        {
            var nodeInstance = nodeInstances[i];
            var nodeInstanceId = NormalizeId(nodeInstance?.id);
            if (string.IsNullOrEmpty(nodeInstanceId))
                continue;

            if (!generatorsById.TryGetValue(nodeInstanceId, out var generatorState))
                continue;

            SimulateGenerator(nodeInstance, generatorState, appliedElapsedSeconds, result);
        }

        return result;
    }

    private void SimulateGenerator(
        NodeInstanceDefinition nodeInstance,
        GameData.GeneratorStateData generatorState,
        double elapsedSeconds,
        OfflineSessionResult result
    )
    {
        if (nodeInstance == null || generatorState == null || result == null)
            return;

        var nodeInstanceId = NormalizeId(nodeInstance.id);
        var automationPurchased =
            generatorState.IsAutomationPurchased || generatorState.IsAutomated;
        var isOwned = generatorState.IsOwned || automationPurchased;
        var isAutomated =
            automationPurchased
            || (modifierService != null && modifierService.IsNodeAutomationEnabled(nodeInstanceId));

        if (!isOwned)
            return;

        var nodeId = NormalizeId(nodeInstance.nodeId);
        if (string.IsNullOrEmpty(nodeId))
            return;

        if (!gameDefinitionService.TryGetNode(nodeId, out var nodeDef) || nodeDef == null)
            return;

        if (!TryResolvePrimaryOutputPerCycle(nodeDef, out var outputResourceId, out var outputPerCycle))
            return;

        var level = Math.Max(1, generatorState.Level);
        var outputMultiplier = SanitizeMultiplier(
            modifierService?.GetNodeOutputMultiplier(nodeInstanceId, outputResourceId) ?? 1d
        );
        var resourceGainMultiplier = SanitizeMultiplier(
            modifierService?.GetResourceGainMultiplier(outputResourceId) ?? 1d
        );
        var speedMultiplier = SanitizeMultiplier(
            modifierService?.GetNodeSpeedMultiplier(nodeInstanceId) ?? 1d
        );
        var cycleDurationSeconds = Math.Max(
            MinCycleDurationSeconds,
            ResolveBaseCycleDuration(nodeDef) / speedMultiplier
        );
        var payoutPerCycle = outputPerCycle * level * outputMultiplier * resourceGainMultiplier;

        var hasRuntimeSnapshot = generatorState.HasRuntimeSnapshot;
        var wasRunning = hasRuntimeSnapshot ? generatorState.WasRunning : isOwned;
        var hasPendingPayout = hasRuntimeSnapshot && generatorState.HasPendingPayout;
        var cycleElapsedSeconds = hasRuntimeSnapshot
            ? SanitizeElapsedSeconds(generatorState.CycleElapsedSeconds)
            : 0d;
        var pendingPayout = hasRuntimeSnapshot
            ? SanitizeElapsedSeconds(generatorState.PendingPayout)
            : 0d;

        if (hasPendingPayout && pendingPayout <= 0d)
        {
            Debug.LogError(
                $"OfflineProgressCalculator[{nodeInstanceId}]: Pending payout was flagged without an amount. Reconstructing one cycle of payout."
            );
            pendingPayout = payoutPerCycle;
        }

        if (hasPendingPayout)
        {
            result.SetGeneratorState(nodeInstanceId, false, true, 0d, pendingPayout);
            return;
        }

        if (isAutomated && !wasRunning)
        {
            Debug.LogError(
                $"OfflineProgressCalculator[{nodeInstanceId}]: Automated generator was saved idle without a pending payout. Resuming continuous automation."
            );
            wasRunning = true;
        }

        if (!wasRunning)
        {
            if (cycleElapsedSeconds > 0d)
            {
                Debug.LogError(
                    $"OfflineProgressCalculator[{nodeInstanceId}]: Non-running generator had saved elapsed time. Clearing elapsed time."
                );
                result.SetGeneratorState(nodeInstanceId, false, false, 0d, 0d);
            }

            return;
        }

        if (cycleElapsedSeconds >= cycleDurationSeconds)
        {
            if (isAutomated)
            {
                Debug.LogError(
                    $"OfflineProgressCalculator[{nodeInstanceId}]: Automated generator had elapsed time beyond a cycle. Preserving only the remainder."
                );
                cycleElapsedSeconds %= cycleDurationSeconds;
            }
            else
            {
                Debug.LogError(
                    $"OfflineProgressCalculator[{nodeInstanceId}]: Manual generator had a completed cycle saved without pending payout. Restoring waiting-to-collect state."
                );
                result.SetGeneratorState(nodeInstanceId, false, true, 0d, payoutPerCycle);
                return;
            }
        }

        var totalElapsedSeconds = cycleElapsedSeconds + elapsedSeconds;
        if (!isAutomated)
        {
            if (totalElapsedSeconds >= cycleDurationSeconds)
            {
                result.SetGeneratorState(nodeInstanceId, false, true, 0d, payoutPerCycle);
                return;
            }

            result.SetGeneratorState(nodeInstanceId, true, false, totalElapsedSeconds, 0d);
            return;
        }

        var completedCycles = (long)Math.Floor(totalElapsedSeconds / cycleDurationSeconds);
        var remainingElapsedSeconds = totalElapsedSeconds - (completedCycles * cycleDurationSeconds);
        if (completedCycles > 0)
            result.AddGain(outputResourceId, payoutPerCycle * completedCycles);

        result.SetGeneratorState(nodeInstanceId, true, false, remainingElapsedSeconds, 0d);
    }

    private static double SanitizeElapsedSeconds(double elapsedSeconds)
    {
        if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds))
            return 0d;

        return Math.Max(0d, elapsedSeconds);
    }

    private static Dictionary<string, GameData.GeneratorStateData> BuildGeneratorStateLookup(
        List<GameData.GeneratorStateData> states
    )
    {
        var map = new Dictionary<string, GameData.GeneratorStateData>(StringComparer.Ordinal);
        if (states == null)
            return map;

        for (int i = 0; i < states.Count; i++)
        {
            var state = states[i];
            var id = NormalizeId(state?.Id);
            if (string.IsNullOrEmpty(id))
                continue;

            if (!map.ContainsKey(id))
                map[id] = state;
        }

        return map;
    }

    private static bool TryResolvePrimaryOutputPerCycle(
        NodeDefinition nodeDef,
        out string resourceId,
        out double outputPerCycle
    )
    {
        resourceId = string.Empty;
        outputPerCycle = 0d;

        if (nodeDef?.outputs == null || nodeDef.outputs.Count == 0)
            return false;

        var output = nodeDef.outputs[0];
        if (output == null)
            return false;

        resourceId = NormalizeId(output.resource);
        if (string.IsNullOrEmpty(resourceId))
            return false;

        var cycleDuration = ResolveBaseCycleDuration(nodeDef);
        if (output.basePayout > 0d)
            outputPerCycle = output.basePayout;
        else if (output.amountPerCycle > 0d)
            outputPerCycle = output.amountPerCycle;
        else if (output.basePerSecond > 0d)
            outputPerCycle = output.basePerSecond * cycleDuration;

        return outputPerCycle > 0d;
    }

    private static double ResolveBaseCycleDuration(NodeDefinition nodeDef)
    {
        return Math.Max(MinCycleDurationSeconds, nodeDef?.cycle?.baseDurationSeconds ?? 1d);
    }

    private static double SanitizeMultiplier(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0d)
            return 1d;

        return value;
    }

    private static string NormalizeId(string id)
    {
        return (id ?? string.Empty).Trim();
    }
}
