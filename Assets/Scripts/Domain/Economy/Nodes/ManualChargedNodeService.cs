using System;
using UniRx;
using UnityEngine;

public sealed class ManualChargedNodeService : IDisposable
{
    private const double Epsilon = 0.0000001d;

    private readonly GeneratorService generatorService;
    private readonly IStateVarService stateVarService;
    private readonly string zoneId;
    private readonly string stateVarId;
    private readonly double maxCharge;
    private readonly double refillRatePerSecond;
    private readonly double spawnCost;
    private readonly double spawnAmount;
    private readonly ReactiveProperty<double> currentCharge;
    private readonly ReadOnlyReactiveProperty<float> chargeNormalized;
    private readonly CompositeDisposable disposables = new();

    private bool isRefillPaused;

    public IReadOnlyReactiveProperty<double> CurrentCharge => currentCharge;
    public IReadOnlyReactiveProperty<float> ChargeNormalized => chargeNormalized;
    public double MaxCharge => maxCharge;

    public ManualChargedNodeService(
        GeneratorService generatorService,
        IStateVarService stateVarService,
        string zoneId,
        string stateVarId,
        double maxCharge,
        double refillRatePerSecond,
        double spawnCost,
        double spawnAmount
    )
    {
        this.generatorService =
            generatorService ?? throw new ArgumentNullException(nameof(generatorService));
        this.stateVarService =
            stateVarService ?? throw new ArgumentNullException(nameof(stateVarService));
        this.zoneId = NormalizeId(zoneId);
        this.stateVarId = NormalizeId(stateVarId);
        this.maxCharge = Math.Max(1d, SanitizeNonNegative(maxCharge));
        this.refillRatePerSecond = SanitizeNonNegative(refillRatePerSecond);
        this.spawnCost = Math.Max(1d, SanitizeNonNegative(spawnCost));
        this.spawnAmount = Math.Max(1d, SanitizeNonNegative(spawnAmount));

        if (string.IsNullOrEmpty(this.zoneId))
            throw new InvalidOperationException(
                "ManualChargedNodeService: zoneId is required."
            );
        if (string.IsNullOrEmpty(this.stateVarId))
        {
            throw new InvalidOperationException(
                "ManualChargedNodeService: stateVarId is required."
            );
        }

        currentCharge = new ReactiveProperty<double>(this.maxCharge).AddTo(disposables);
        chargeNormalized = currentCharge
            .Select(value => Mathf.Clamp01((float)(value / this.maxCharge)))
            .DistinctUntilChanged()
            .ToReadOnlyReactiveProperty()
            .AddTo(disposables);

        Observable.EveryUpdate().Subscribe(_ => RefillTick()).AddTo(disposables);
    }

    public bool TrySpawnOnce()
    {
        RefillTick();

        if (!generatorService.IsOwned.Value)
            return false;

        if (currentCharge.Value + Epsilon < spawnCost)
            return false;

        var capacity = stateVarService.GetCapacity(zoneId, stateVarId);
        if (capacity <= 0d)
            return false;

        var quantity = stateVarService.GetQuantity(zoneId, stateVarId);
        if (quantity + spawnAmount > capacity + Epsilon)
            return false;

        SetCurrentCharge(currentCharge.Value - spawnCost);
        stateVarService.AddQuantity(zoneId, stateVarId, spawnAmount);
        return true;
    }

    public void SetRefillPaused(bool paused)
    {
        if (isRefillPaused == paused)
            return;

        RefillTick();
        isRefillPaused = paused;
    }

    public void Dispose()
    {
        disposables.Dispose();
    }

    private void RefillTick()
    {
        if (isRefillPaused || !generatorService.IsOwned.Value)
            return;

        if (currentCharge.Value >= maxCharge - Epsilon || refillRatePerSecond <= Epsilon)
            return;

        var deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
            return;

        SetCurrentCharge(currentCharge.Value + refillRatePerSecond * deltaTime);
    }

    private void SetCurrentCharge(double value)
    {
        var clamped = SanitizeNonNegative(value);
        if (clamped > maxCharge)
            clamped = maxCharge;

        if (Math.Abs(currentCharge.Value - clamped) < Epsilon)
            return;

        currentCharge.Value = clamped;
    }

    private static double SanitizeNonNegative(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0d;

        return Math.Max(0d, value);
    }

    private static string NormalizeId(string value)
    {
        return (value ?? string.Empty).Trim();
    }
}
