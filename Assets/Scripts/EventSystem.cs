using System;
using UniRx;

public static class EventSystem
{
    public static readonly Subject<(string resourceId, double amount)> OnIncrementBalance =
        new Subject<(string resourceId, double amount)>();
}
