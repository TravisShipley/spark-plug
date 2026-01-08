using UniRx;
using System;

public class TickService : IDisposable
{
    public TimeSpan Interval { get; }
    public IObservable<long> OnTick { get; }

    public TickService(TimeSpan tickInterval)
    {
        Interval = tickInterval;
        OnTick = Observable.Interval(tickInterval).Share();
    }

    public void Dispose() { }
}