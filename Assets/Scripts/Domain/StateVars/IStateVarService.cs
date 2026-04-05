using UniRx;

public interface IStateVarService
{
    double GetQuantity(string zoneId, string varId);
    double GetCapacity(string zoneId, string varId);
    IReadOnlyReactiveProperty<double> ObserveQuantity(string zoneId, string varId);
    void SetQuantity(string zoneId, string varId, double value);
    void AddQuantity(string zoneId, string varId, double delta);
    void RegisterTransfer(string zoneId, string fromVarId, string toVarId, double ratePerSecond);
    void UnregisterTransfer(string zoneId, string fromVarId, string toVarId);
    void TickTransfers(double deltaTimeSeconds);
}
