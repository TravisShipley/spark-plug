using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class RewardService
{
    private readonly WalletService walletService;
    private readonly ResourceCatalog resourceCatalog;
    private readonly Dictionary<string, RewardPoolDefinition> rewardPoolsById = new(
        StringComparer.Ordinal
    );

    public RewardService(GameDefinitionService gameDefinitionService, WalletService walletService)
    {
        if (gameDefinitionService == null)
            throw new ArgumentNullException(nameof(gameDefinitionService));

        this.walletService = walletService ?? throw new ArgumentNullException(nameof(walletService));
        resourceCatalog = gameDefinitionService.ResourceCatalog;

        var rewardPools = gameDefinitionService.RewardPools;
        if (rewardPools == null)
            return;

        for (int i = 0; i < rewardPools.Count; i++)
        {
            var rewardPool = rewardPools[i];
            if (rewardPool == null)
                continue;

            var rewardPoolId = NormalizeId(rewardPool.id);
            if (string.IsNullOrEmpty(rewardPoolId))
                throw new InvalidOperationException($"rewardPools[{i}].id is empty.");

            if (rewardPoolsById.ContainsKey(rewardPoolId))
                throw new InvalidOperationException($"Duplicate rewardPool id '{rewardPoolId}'.");

            rewardPoolsById[rewardPoolId] = rewardPool;
        }
    }

    public void Roll(string rewardPoolId)
    {
        var id = NormalizeId(rewardPoolId);
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("RewardService.Roll: rewardPoolId is empty.");

        if (!rewardPoolsById.TryGetValue(id, out var rewardPool) || rewardPool == null)
            throw new InvalidOperationException($"Unknown rewardPool id '{id}'.");

        var rewards = rewardPool.rewards;
        if (rewards == null || rewards.Length == 0)
            throw new InvalidOperationException($"RewardPool '{id}' has no rewards.");

        var chosenIndex = RollWeightedIndex(rewards, id);
        var chosenEntry = rewards[chosenIndex];
        if (chosenEntry == null || chosenEntry.action == null)
        {
            throw new InvalidOperationException(
                $"RewardPool '{id}' selected reward index {chosenIndex} with no action."
            );
        }

        ExecuteAction(id, chosenEntry.action);
    }

    private void ExecuteAction(string rewardPoolId, RewardActionDefinition action)
    {
        var actionType = NormalizeId(action.type);
        if (string.IsNullOrEmpty(actionType))
        {
            throw new InvalidOperationException(
                $"RewardPool '{rewardPoolId}' has reward action with empty type."
            );
        }

        if (!string.Equals(actionType, "grantResource", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"RewardPool '{rewardPoolId}' has unsupported reward action '{actionType}'. "
                    + "Only 'grantResource' is supported in this slice."
            );
        }

        var resourceId = NormalizeId(action.resourceId);
        if (string.IsNullOrEmpty(resourceId))
        {
            throw new InvalidOperationException(
                $"RewardPool '{rewardPoolId}' grantResource action has empty resourceId."
            );
        }

        if (resourceCatalog != null && !resourceCatalog.TryGet(resourceId, out _))
        {
            throw new InvalidOperationException(
                $"RewardPool '{rewardPoolId}' grantResource action references unknown resource '{resourceId}'."
            );
        }

        walletService.AddRaw(resourceId, action.amount);

        Debug.Log(
            $"[RewardPool] Rolled '{rewardPoolId}' -> grantResource {resourceId} +{action.amount.ToString("0.###")}."
        );
    }

    private static int RollWeightedIndex(RewardEntryDefinition[] rewards, string rewardPoolId)
    {
        var totalWeight = 0f;
        for (int i = 0; i < rewards.Length; i++)
        {
            var entry = rewards[i];
            if (entry == null)
                continue;

            var weight = entry.weight > 0f ? entry.weight : 1f;
            totalWeight += weight;
        }

        if (totalWeight <= 0f)
        {
            throw new InvalidOperationException(
                $"RewardPool '{rewardPoolId}' has invalid total reward weight."
            );
        }

        var roll = UnityEngine.Random.Range(0f, totalWeight);
        var cumulative = 0f;
        for (int i = 0; i < rewards.Length; i++)
        {
            var entry = rewards[i];
            if (entry == null)
                continue;

            var weight = entry.weight > 0f ? entry.weight : 1f;
            cumulative += weight;
            if (roll <= cumulative)
                return i;
        }

        return rewards.Length - 1;
    }

    private static string NormalizeId(string raw)
    {
        return (raw ?? string.Empty).Trim();
    }
}
