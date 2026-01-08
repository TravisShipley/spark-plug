using System;
using UniRx;

public static class EventSystem
{
    public static readonly Subject<(CurrencyType type, double amount)> OnIncrementBalance = new Subject<(CurrencyType, double)>();
}