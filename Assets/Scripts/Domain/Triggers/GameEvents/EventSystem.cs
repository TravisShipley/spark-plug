using System;
using UniRx;

public sealed class GameEventStream : IDisposable
{
    public readonly struct MilestoneFiredEvent
    {
        public readonly string milestoneId;
        public readonly string nodeId;
        public readonly string zoneId;
        public readonly int atLevel;

        public MilestoneFiredEvent(string milestoneId, string nodeId, string zoneId, int atLevel)
        {
            this.milestoneId = milestoneId;
            this.nodeId = nodeId;
            this.zoneId = zoneId;
            this.atLevel = atLevel;
        }
    }

    private readonly Subject<(string resourceId, double amount)> incrementBalance = new();
    private readonly Subject<MilestoneFiredEvent> milestoneFired = new();
    private readonly Subject<Unit> resetSaveRequested = new();

    public IObservable<(string resourceId, double amount)> IncrementBalance => incrementBalance;
    public IObservable<MilestoneFiredEvent> MilestoneFired => milestoneFired;
    public IObservable<Unit> ResetSaveRequested => resetSaveRequested;

    public void PublishIncrementBalance(string resourceId, double amount)
    {
        var normalizedResourceId = (resourceId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalizedResourceId))
            throw new InvalidOperationException("GameEventStream: resourceId cannot be empty.");

        incrementBalance.OnNext((normalizedResourceId, amount));
    }

    public void PublishMilestoneFired(MilestoneFiredEvent evt)
    {
        milestoneFired.OnNext(evt);
    }

    public void RequestResetSave()
    {
        resetSaveRequested.OnNext(Unit.Default);
    }

    public void Dispose()
    {
        incrementBalance.Dispose();
        milestoneFired.Dispose();
        resetSaveRequested.Dispose();
    }
}
