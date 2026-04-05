using System;
using UniRx;
using UnityEngine;

public sealed class ManualChargedNodeService : IDisposable
{
    private const double Epsilon = 0.0000001d;

    private readonly GeneratorService generatorService;
    private readonly IStateVarService stateVarService;
    private readonly string zoneId;
    private readonly string bufferVarId;
    private readonly string outputVarId;
    private readonly double refillRatePerSecond;
    private readonly double spawnCost;
    private readonly double spawnAmount;
    private readonly IReadOnlyReactiveProperty<double> currentCharge;
    private readonly ReadOnlyReactiveProperty<float> chargeNormalized;
    private readonly CompositeDisposable disposables = new();

    private bool isRefillPaused;

    public IReadOnlyReactiveProperty<double> CurrentCharge => currentCharge;
    public IReadOnlyReactiveProperty<float> ChargeNormalized => chargeNormalized;
    public double MaxCharge => stateVarService.GetCapacity(zoneId, bufferVarId);

    public ManualChargedNodeService(
        GeneratorService generatorService,
        IStateVarService stateVarService,
        string zoneId,
        string bufferVarId,
        string outputVarId,
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
        this.bufferVarId = NormalizeId(bufferVarId);
        this.outputVarId = NormalizeId(outputVarId);
        this.refillRatePerSecond = SanitizeNonNegative(refillRatePerSecond);
        this.spawnCost = Math.Max(1d, SanitizeNonNegative(spawnCost));
        this.spawnAmount = Math.Max(1d, SanitizeNonNegative(spawnAmount));

        if (string.IsNullOrEmpty(this.zoneId))
            throw new InvalidOperationException(
                "ManualChargedNodeService: zoneId is required."
            );
        if (string.IsNullOrEmpty(this.bufferVarId))
        {
            throw new InvalidOperationException(
                "ManualChargedNodeService: bufferVarId is required."
            );
        }
        if (string.IsNullOrEmpty(this.outputVarId))
            throw new InvalidOperationException("ManualChargedNodeService: outputVarId is required.");

        currentCharge = stateVarService.ObserveQuantity(this.zoneId, this.bufferVarId);
        chargeNormalized = currentCharge
            .Select(value =>
            {
                var maxCharge = MaxCharge;
                if (maxCharge <= Epsilon)
                    return 0f;

                return Mathf.Clamp01((float)(value / maxCharge));
            })
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

        var currentBuffer = stateVarService.GetQuantity(zoneId, bufferVarId);
        if (currentBuffer + Epsilon < spawnCost)
            return false;

        var outputCapacity = stateVarService.GetCapacity(zoneId, outputVarId);
        if (outputCapacity <= 0d)
            return false;

        var outputQuantity = stateVarService.GetQuantity(zoneId, outputVarId);
        if (outputQuantity + spawnAmount > outputCapacity + Epsilon)
            return false;

        stateVarService.AddQuantity(zoneId, bufferVarId, -spawnCost);
        stateVarService.AddQuantity(zoneId, outputVarId, spawnAmount);
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

        if (currentCharge.Value >= MaxCharge - Epsilon || refillRatePerSecond <= Epsilon)
            return;

        var deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
            return;

        stateVarService.AddQuantity(zoneId, bufferVarId, refillRatePerSecond * deltaTime);
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
