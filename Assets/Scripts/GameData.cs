using System;
using System.Collections.Generic;

[System.Serializable]
public class GameData
{
    public double CashAmount;
    public double GoldAmount;

    public double rateOfIncome = 1f;

    [Serializable]
    public class GeneratorStateData
    {
        public string Id;
        public bool IsOwned;
        public bool IsAutomated;
        public int Level;
    }

    public List<GeneratorStateData> Generators = new();
}