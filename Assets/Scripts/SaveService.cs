using System;
using System.Collections.Generic;
using UniRx;

/// <summary>
/// Owns an in-memory GameData snapshot and performs debounced writes to disk.
/// </summary>
public sealed class SaveService : IDisposable
{
    private readonly CompositeDisposable disposables = new();
    private readonly Subject<Unit> saveRequests = new();
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    public GameData Data { get; private set; }

    public SaveService()
    {
        saveRequests
            .Throttle(Debounce)
            .Subscribe(_ =>
            {
                if (Data != null)
                    SaveSystem.SaveGame(Data);
            })
            .AddTo(disposables);

        SaveSystem.OnSaveReset += OnSaveReset;
    }

    public void Load()
    {
        Data = SaveSystem.LoadGame() ?? new GameData();
        Data.Generators ??= new List<GameData.GeneratorStateData>();
        Data.Upgrades ??= new List<GameData.UpgradeStateData>();
    }

    public void RequestSave()
    {
        if (Data == null)
            return;

        saveRequests.OnNext(Unit.Default);
    }

    public void SaveNow()
    {
        if (Data == null)
            return;

        SaveSystem.SaveGame(Data);
    }

    public void SetGeneratorState(string id, int level, bool isOwned, bool isAutomated)
    {
        if (Data == null)
            return;

        Data.Generators ??= new List<GameData.GeneratorStateData>();

        var entry = Data.Generators.Find(g => g != null && g.Id == id);
        if (entry == null)
        {
            entry = new GameData.GeneratorStateData { Id = id };
            Data.Generators.Add(entry);
        }

        entry.Level = level;
        entry.IsOwned = isOwned;
        entry.IsAutomated = isAutomated;

        RequestSave();
    }

    private void OnSaveReset(GameData _)
    {
        // Reset in-memory snapshot to a clean state.
        Data = new GameData();
        Data.Generators ??= new List<GameData.GeneratorStateData>();
        Data.Upgrades ??= new List<GameData.UpgradeStateData>();
    }

    public void Dispose()
    {
        SaveSystem.OnSaveReset -= OnSaveReset;

        // Flush best-effort
        SaveNow();

        saveRequests.OnCompleted();
        saveRequests.Dispose();
        disposables.Dispose();
    }
}
