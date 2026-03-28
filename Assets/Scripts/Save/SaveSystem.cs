using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

public static class SaveSystem
{
    public static bool HasSave(string saveKey)
    {
        return File.Exists(GetSavePath(saveKey));
    }

    public static void SaveGame(GameData data, string saveKey)
    {
        try
        {
            if (data == null)
                data = CreateNewGameData();

            data.EnsureInitialized();

            string json = JsonUtility.ToJson(data, true);
            string savePath = GetSavePath(saveKey);

            // Atomic write: write to temp, then replace
            string dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tempPath = savePath + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(savePath))
                File.Replace(tempPath, savePath, null);
            else
                File.Move(tempPath, savePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save game data: {e.Message}");
        }
    }

    public static GameData LoadGame(string saveKey)
    {
        string savePath = GetSavePath(saveKey);
        if (!File.Exists(savePath))
            return null;

        try
        {
            string json = File.ReadAllText(savePath);
            var data = JsonUtility.FromJson<GameData>(json);
            if (data == null)
            {
                Debug.LogError("Failed to load or parse save data: JSON produced null GameData.");
                return null;
            }

            data.EnsureInitialized();
            return data;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load or parse save data: {e.Message}");
            return null;
        }
    }

    public static void DeleteSaveFile(string saveKey)
    {
        string savePath = GetSavePath(saveKey);
        try
        {
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
                Debug.Log($"SaveSystem: Save data cleared at '{savePath}'.");
            }
            else
            {
                Debug.LogWarning($"SaveSystem: No save data to clear at '{savePath}'.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to clear save data: {e.Message}");
        }
    }

    private static string GetSavePath(string saveKey)
    {
        var normalizedKey = string.IsNullOrWhiteSpace(saveKey)
            ? SparkPlugSaveKey.Compose(
                SparkPlugSaveKey.DefaultSessionId,
                SparkPlugSaveKey.DefaultSaveSlotId
            )
            : saveKey.Trim();
        return Path.Combine(
            Application.persistentDataPath,
            MakeSafeFileName(normalizedKey) + ".json"
        );
    }

    private static string MakeSafeFileName(string value)
    {
        var chars = value.ToCharArray();
        var invalidChars = Path.GetInvalidFileNameChars();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalidChars, chars[i]) >= 0)
                chars[i] = '_';
        }

        return new string(chars);
    }

    private static GameData CreateNewGameData()
    {
        var data = new GameData
        {
            Generators = new List<GameData.GeneratorStateData>(),
            Upgrades = new List<GameData.UpgradeStateData>(),
            Resources = new List<GameData.ResourceBalanceData>(),
            LifetimeEarnings = new List<GameData.LifetimeEarningData>(),
            ZoneStates = new List<GameData.ZoneStateData>(),
            ActiveBuffId = string.Empty,
            ActiveBuffExpiresAtUnixSeconds = 0,
            lastSeenUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        data.EnsureInitialized();
        return data;
    }
}
