using System;

[Serializable]
public class GeneratorModel
{
    public string Id;

    public int Level = 0;
    public bool IsOwned;
    public bool IsAutomated;

    // runtime-only (not serialized)
    [NonSerialized] public double CycleElapsedSeconds;
}
