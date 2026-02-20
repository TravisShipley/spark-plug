#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SparkPlugSmokeTestRunner
{
    private const string GameDefinitionPath = "Assets/Data/game_definition.json";
    private const int MaxUpgradeEntriesToPrint = 20;

    [MenuItem("SparkPlug/Smoke Test/Reset Save")]
    public static void ResetSave()
    {
        SaveSystem.DeleteSaveFile();
        Debug.Log(
            $"[SmokeTest] Save reset complete. persistentDataPath='{Application.persistentDataPath}'."
        );
    }

    [MenuItem("SparkPlug/Smoke Test/Print Current State")]
    public static void PrintCurrentState()
    {
        GameDefinition definition;
        try
        {
            definition = GameDefinitionLoader.LoadFromFile(GameDefinitionPath);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SmokeTest] Failed to load game definition: {ex.Message}");
            return;
        }

        var data = SaveSystem.LoadGame();
        var usingDefaults = false;
        if (data == null)
        {
            usingDefaults = true;
            data = CreateDefaultData(definition);
        }

        data.EnsureInitialized();
        Debug.Log(BuildReport(definition, data, usingDefaults));
    }

    private static GameData CreateDefaultData(GameDefinition definition)
    {
        var saveService = new SaveService();
        try
        {
            return saveService.CreateDefaultSaveData(definition);
        }
        finally
        {
            saveService.Dispose();
        }
    }

    private static string BuildReport(GameDefinition definition, GameData data, bool usingDefaults)
    {
        var report = new StringBuilder(2048);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var activeBuffId = NormalizeId(data.ActiveBuffId);
        var buffRemaining = string.IsNullOrEmpty(activeBuffId)
            ? 0
            : Math.Max(0, data.ActiveBuffExpiresAtUnixSeconds - now);

        report.AppendLine("=== Spark Plug Smoke Test State ===");
        report.AppendLine($"Source: {(usingDefaults ? "content defaults (no save file)" : "save.json")}");
        report.AppendLine($"persistentDataPath: {Application.persistentDataPath}");
        report.AppendLine($"CurrentZone: {ResolveCurrentZone(definition, data)}");
        report.AppendLine(
            string.IsNullOrEmpty(activeBuffId)
                ? "ActiveBuff: none"
                : $"ActiveBuff: {activeBuffId} ({buffRemaining}s remaining)"
        );
        report.AppendLine();

        AppendNodeSection(report, definition, data);
        AppendUpgradeSection(report, data);
        AppendUnlockedSection(report, data);
        AppendAutomationSection(report, data);
        AppendMilestoneSection(report, data);

        report.AppendLine("===================================");
        return report.ToString();
    }

    private static void AppendNodeSection(StringBuilder report, GameDefinition definition, GameData data)
    {
        report.AppendLine("NodeInstance States:");

        var generatorById = new Dictionary<string, GameData.GeneratorStateData>(StringComparer.Ordinal);
        if (data.Generators != null)
        {
            for (int i = 0; i < data.Generators.Count; i++)
            {
                var state = data.Generators[i];
                var id = NormalizeId(state?.Id);
                if (string.IsNullOrEmpty(id) || generatorById.ContainsKey(id))
                    continue;

                generatorById[id] = state;
            }
        }

        var milestoneLevelsByNodeId = BuildMilestoneLevelsByNodeId(definition);
        var nodeInstances = new List<NodeInstanceDefinition>();
        if (definition?.nodeInstances != null)
            nodeInstances.AddRange(definition.nodeInstances);

        nodeInstances.Sort(
            (a, b) =>
                string.Compare(
                    NormalizeId(a?.id),
                    NormalizeId(b?.id),
                    StringComparison.Ordinal
                )
        );

        for (int i = 0; i < nodeInstances.Count; i++)
        {
            var instance = nodeInstances[i];
            var instanceId = NormalizeId(instance?.id);
            if (string.IsNullOrEmpty(instanceId))
                continue;

            var hasSavedState = generatorById.TryGetValue(instanceId, out var saved);
            var initialEnabled = instance?.initialState?.enabled ?? false;
            var owned = hasSavedState ? saved.IsOwned : initialEnabled;
            var enabled = hasSavedState ? saved.IsEnabled : initialEnabled;
            var automation = hasSavedState
                ? (saved.IsAutomationPurchased || saved.IsAutomated)
                : false;
            var level = hasSavedState ? saved.Level : Math.Max(0, instance?.initialState?.level ?? 0);

            if (automation)
            {
                owned = true;
                enabled = true;
            }

            if (owned)
                enabled = true;

            if (owned && level < 1)
                level = 1;
            if (!owned)
                level = 0;

            var nodeId = NormalizeId(instance?.nodeId);
            var milestoneRank = ComputeMilestoneRank(level, nodeId, milestoneLevelsByNodeId);

            report.AppendLine(
                $"- {instanceId} | level={level} | owned={owned} | enabled={enabled} | automation={automation} | milestoneRank={milestoneRank}"
            );
        }

        report.AppendLine();
    }

    private static void AppendUpgradeSection(StringBuilder report, GameData data)
    {
        var upgrades = new List<GameData.UpgradeStateData>();
        if (data.Upgrades != null)
            upgrades.AddRange(data.Upgrades);

        upgrades.Sort(
            (a, b) =>
                string.Compare(NormalizeId(a?.Id), NormalizeId(b?.Id), StringComparison.Ordinal)
        );

        int purchasedCount = 0;
        for (int i = 0; i < upgrades.Count; i++)
        {
            var entry = upgrades[i];
            if (entry == null)
                continue;

            if (entry.PurchasedCount > 0 && !string.IsNullOrEmpty(NormalizeId(entry.Id)))
                purchasedCount++;
        }

        report.AppendLine($"Purchased Upgrades: {purchasedCount}");
        if (purchasedCount <= 0)
        {
            report.AppendLine("- (none)");
            report.AppendLine();
            return;
        }

        int printed = 0;
        for (int i = 0; i < upgrades.Count; i++)
        {
            var entry = upgrades[i];
            if (entry == null)
                continue;

            var id = NormalizeId(entry.Id);
            if (string.IsNullOrEmpty(id) || entry.PurchasedCount <= 0)
                continue;

            report.AppendLine($"- {id} (rank={entry.PurchasedCount})");
            printed++;
            if (printed >= MaxUpgradeEntriesToPrint)
                break;
        }

        if (purchasedCount > MaxUpgradeEntriesToPrint)
            report.AppendLine($"- ... ({purchasedCount - MaxUpgradeEntriesToPrint} more)");

        report.AppendLine();
    }

    private static void AppendUnlockedSection(StringBuilder report, GameData data)
    {
        report.AppendLine("Unlocked NodeInstances:");
        if (data.UnlockedNodeInstanceIds == null || data.UnlockedNodeInstanceIds.Count == 0)
        {
            report.AppendLine("- (none)");
            report.AppendLine();
            return;
        }

        var unlocked = new List<string>(data.UnlockedNodeInstanceIds);
        unlocked.Sort(StringComparer.Ordinal);

        for (int i = 0; i < unlocked.Count; i++)
            report.AppendLine($"- {unlocked[i]}");

        report.AppendLine();
    }

    private static void AppendAutomationSection(StringBuilder report, GameData data)
    {
        report.AppendLine("Automation Purchased:");
        var automated = new List<string>();

        if (data.Generators != null)
        {
            for (int i = 0; i < data.Generators.Count; i++)
            {
                var entry = data.Generators[i];
                if (entry == null)
                    continue;

                if (!(entry.IsAutomationPurchased || entry.IsAutomated))
                    continue;

                var id = NormalizeId(entry.Id);
                if (!string.IsNullOrEmpty(id))
                    automated.Add(id);
            }
        }

        if (automated.Count == 0)
        {
            report.AppendLine("- (none)");
            report.AppendLine();
            return;
        }

        automated.Sort(StringComparer.Ordinal);
        for (int i = 0; i < automated.Count; i++)
            report.AppendLine($"- {automated[i]}");

        report.AppendLine();
    }

    private static void AppendMilestoneSection(StringBuilder report, GameData data)
    {
        report.AppendLine("Fired Milestones:");
        if (data.FiredMilestoneIds == null || data.FiredMilestoneIds.Count == 0)
        {
            report.AppendLine("- (none)");
            report.AppendLine();
            return;
        }

        var milestones = new List<string>(data.FiredMilestoneIds);
        milestones.Sort(StringComparer.Ordinal);

        for (int i = 0; i < milestones.Count; i++)
            report.AppendLine($"- {milestones[i]}");

        report.AppendLine();
    }

    private static Dictionary<string, List<int>> BuildMilestoneLevelsByNodeId(GameDefinition definition)
    {
        var byNode = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        if (definition?.milestones == null)
            return byNode;

        for (int i = 0; i < definition.milestones.Count; i++)
        {
            var milestone = definition.milestones[i];
            var nodeId = NormalizeId(milestone?.nodeId);
            if (string.IsNullOrEmpty(nodeId) || milestone.atLevel <= 0)
                continue;

            if (!byNode.TryGetValue(nodeId, out var levels))
            {
                levels = new List<int>();
                byNode[nodeId] = levels;
            }

            levels.Add(milestone.atLevel);
        }

        foreach (var kvp in byNode)
            kvp.Value.Sort();

        return byNode;
    }

    private static int ComputeMilestoneRank(
        int level,
        string nodeId,
        Dictionary<string, List<int>> milestoneLevelsByNodeId
    )
    {
        if (!milestoneLevelsByNodeId.TryGetValue(nodeId, out var levels) || levels == null)
            return 0;

        int rank = 0;
        for (int i = 0; i < levels.Count; i++)
        {
            if (level >= levels[i])
                rank++;
        }

        return rank;
    }

    private static string ResolveCurrentZone(GameDefinition definition, GameData data)
    {
        var nodeInstancesById = new Dictionary<string, NodeInstanceDefinition>(StringComparer.Ordinal);
        if (definition?.nodeInstances != null)
        {
            for (int i = 0; i < definition.nodeInstances.Count; i++)
            {
                var instance = definition.nodeInstances[i];
                var id = NormalizeId(instance?.id);
                if (string.IsNullOrEmpty(id) || nodeInstancesById.ContainsKey(id))
                    continue;

                nodeInstancesById[id] = instance;
            }
        }

        if (data.UnlockedNodeInstanceIds != null)
        {
            foreach (var unlockedId in data.UnlockedNodeInstanceIds)
            {
                var normalizedId = NormalizeId(unlockedId);
                if (string.IsNullOrEmpty(normalizedId))
                    continue;

                if (!nodeInstancesById.TryGetValue(normalizedId, out var unlocked))
                    continue;

                var unlockedZone = NormalizeId(unlocked?.zoneId);
                if (!string.IsNullOrEmpty(unlockedZone))
                    return unlockedZone;
            }
        }

        if (definition?.nodeInstances != null)
        {
            for (int i = 0; i < definition.nodeInstances.Count; i++)
            {
                var instance = definition.nodeInstances[i];
                var zoneId = NormalizeId(instance?.zoneId);
                if (!string.IsNullOrEmpty(zoneId))
                    return zoneId;
            }
        }

        return "unknown";
    }

    private static string NormalizeId(string raw)
    {
        return (raw ?? string.Empty).Trim();
    }
}
#endif
