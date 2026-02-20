using System;
using System.Collections.Generic;

public sealed class OfflineProgressCalculator
{
    private const string CurrencySoftResourceId = "currencySoft";
    private const double MinCycleDurationSeconds = 0.0001d;

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
        var clampedSecondsAway = Math.Max(0, secondsAway);
        var result = new OfflineSessionResult { secondsAway = clampedSecondsAway };
        if (clampedSecondsAway <= 0 || saveData == null)
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

            var automationPurchased =
                generatorState.IsAutomationPurchased || generatorState.IsAutomated;
            var isOwned = generatorState.IsOwned || automationPurchased;
            var isAutomated =
                automationPurchased
                || (modifierService != null && modifierService.IsNodeAutomationEnabled(nodeInstanceId));

            if (!isOwned || !isAutomated)
                continue;

            var nodeId = NormalizeId(nodeInstance.nodeId);
            if (string.IsNullOrEmpty(nodeId))
                continue;

            if (!gameDefinitionService.TryGetNode(nodeId, out var nodeDef) || nodeDef == null)
                continue;

            if (!TryResolveCurrencySoftOutputPerCycle(nodeDef, out var outputPerCycle))
                continue;

            var level = Math.Max(1, generatorState.Level);
            var outputMultiplier = SanitizeMultiplier(
                modifierService?.GetNodeOutputMultiplier(nodeInstanceId, CurrencySoftResourceId) ?? 1d
            );

            var speedMultiplier = SanitizeMultiplier(
                modifierService?.GetNodeSpeedMultiplier(nodeInstanceId) ?? 1d
            );
            var baseCycleDuration = ResolveBaseCycleDuration(nodeDef);
            var cycleDurationSeconds = Math.Max(
                MinCycleDurationSeconds,
                baseCycleDuration / speedMultiplier
            );
            var cycles = (long)Math.Floor(clampedSecondsAway / cycleDurationSeconds);
            if (cycles <= 0)
                continue;

            var resourceGainMultiplier = modifierService?.GetResourceGainMultiplier(
                CurrencySoftResourceId
            ) ?? 1d;
            if (double.IsNaN(resourceGainMultiplier) || double.IsInfinity(resourceGainMultiplier))
                resourceGainMultiplier = 1d;

            var gain =
                outputPerCycle * level * outputMultiplier * cycles * resourceGainMultiplier;
            result.AddGain(CurrencySoftResourceId, gain);
        }

        return result;
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

    private static bool TryResolveCurrencySoftOutputPerCycle(
        NodeDefinition nodeDef,
        out double outputPerCycle
    )
    {
        outputPerCycle = 0d;
        if (nodeDef?.outputs == null || nodeDef.outputs.Count == 0)
            return false;

        NodeOutputDefinition output = null;
        for (int i = 0; i < nodeDef.outputs.Count; i++)
        {
            var candidate = nodeDef.outputs[i];
            if (candidate == null)
                continue;

            var resourceId = NormalizeId(candidate.resource);
            if (string.Equals(resourceId, CurrencySoftResourceId, StringComparison.Ordinal))
            {
                output = candidate;
                break;
            }
        }

        if (output == null)
            return false;

        var cycleDuration = ResolveBaseCycleDuration(nodeDef);
        var payout = output.basePayout;
        var amountPerCycle = output.amountPerCycle;
        var perSecond = output.basePerSecond;

        if (payout > 0d)
            outputPerCycle = payout;
        else if (amountPerCycle > 0d)
            outputPerCycle = amountPerCycle;
        else if (perSecond > 0d)
            outputPerCycle = perSecond * cycleDuration;
        else
            outputPerCycle = 0d;

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
