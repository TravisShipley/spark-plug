using UniRx;

public interface IStateVarService
{
    double GetQuantity(string zoneId, string varId);
    double GetCapacity(string zoneId, string varId);
    IReadOnlyReactiveProperty<double> ObserveQuantity(string zoneId, string varId);
    void SetQuantity(string zoneId, string varId, double value);
    void AddQuantity(string zoneId, string varId, double delta);
}
