using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

public static class SaveSystem
{
    public static event Action<GameData> OnSaveReset;

    private static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

    public static void SaveGame(GameData data)
    {
        try
        {
            if (data == null)
                data = CreateNewGameData();

            data.Generators ??= new List<GameData.GeneratorStateData>();

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
        if (File.Exists(SavePath))
        {
            try
            {
                string json = File.ReadAllText(SavePath);

                var data = JsonUtility.FromJson<GameData>(json);

                // Migration / null-safety for older saves
                if (data == null)
                    return CreateNewGameData();

                data.Generators ??= new List<GameData.GeneratorStateData>();

                return data;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load or parse save data: {e.Message}");
                return CreateNewGameData();
            }
        }
        return CreateNewGameData();
    }

    public static void ClearSave()
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

    public static GameData ResetSave()
    {
        ClearSave();
        GameData defaultData = CreateNewGameData();
        Debug.Log("SaveSystem: Resetting save data to defaults.");
        SaveGame(defaultData);
        OnSaveReset?.Invoke(defaultData);
        return defaultData;
    }

    private static GameData CreateNewGameData()
    {
        return new GameData
        {
            Generators = new List<GameData.GeneratorStateData>()
        };
    }
}