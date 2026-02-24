using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

public static class SaveSystem
{
    private static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

    public static bool HasSave()
    {
        return File.Exists(SavePath);
    }

    public static void SaveGame(GameData data)
    {
        try
        {
            if (data == null)
                data = CreateNewGameData();

            data.EnsureInitialized();

            string json = JsonUtility.ToJson(data, true);

            // Atomic write: write to temp, then replace
            string dir = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tempPath = SavePath + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(SavePath))
                File.Replace(tempPath, SavePath, null);
            else
                File.Move(tempPath, SavePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save game data: {e.Message}");
        }
    }

    public static GameData LoadGame()
    {
        if (!File.Exists(SavePath))
            return null;

        try
        {
            string json = File.ReadAllText(SavePath);
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

    public static void DeleteSaveFile()
    {
        try
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
                Debug.Log($"SaveSystem: Save data cleared at '{SavePath}'.");
            }
            else
            {
                Debug.LogWarning($"SaveSystem: No save data to clear at '{SavePath}'.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to clear save data: {e.Message}");
        }
    }

    private static GameData CreateNewGameData()
    {
        var data = new GameData
        {
            Generators = new List<GameData.GeneratorStateData>(),
            Upgrades = new List<GameData.UpgradeStateData>(),
            Resources = new List<GameData.ResourceBalanceData>(),
            LifetimeEarnings = new List<GameData.LifetimeEarningData>(),
            ActiveBuffId = string.Empty,
            ActiveBuffExpiresAtUnixSeconds = 0,
            lastSeenUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        data.EnsureInitialized();
        return data;
    }
}
