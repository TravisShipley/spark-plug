using System;
using UniRx;

public static class EventSystem
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

    public static readonly Subject<(string resourceId, double amount)> OnIncrementBalance =
        new Subject<(string resourceId, double amount)>();
    public static readonly Subject<MilestoneFiredEvent> OnMilestoneFired =
        new Subject<MilestoneFiredEvent>();

    public static readonly Subject<Unit> OnResetSaveRequested = new Subject<Unit>();
}
