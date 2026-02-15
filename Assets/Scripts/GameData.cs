using System;
using System.Collections.Generic;

[System.Serializable]
public class GameData
{
    [Serializable]
    public class ResourceBalanceData
    {
        public string ResourceId;
        public double Amount;
    }

    public double globalIncomeMultiplier = 1.0;

    [Serializable]
    public class GeneratorStateData
    {
        public string Id;
        public bool IsOwned;
        public bool IsAutomated;
        public int Level;
    }

    [Serializable]
    public class UpgradeStateData
    {
        // Stable upgrade identifier (matches UpgradeEntry.id in game definition content)
        public string Id;

        // Number of times purchased. For one-time upgrades, this will be 0 or 1.
        public int PurchasedCount;
    }

    public List<GeneratorStateData> Generators = new();
    public List<UpgradeStateData> Upgrades = new();
    public List<ResourceBalanceData> Resources = new();
}
