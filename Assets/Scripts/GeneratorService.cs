using System;
using UniRx;
using UnityEngine;

public class GeneratorService : IDisposable
{
    private readonly GeneratorModel model;
    private readonly GeneratorDefinition definition;
    private readonly WalletService wallet;
    private readonly TickService tickService;

    private double lastIntervalSeconds;

    private readonly CompositeDisposable disposables = new();
    private readonly ReactiveProperty<double> cycleProgress = new(0);
    public IReadOnlyReactiveProperty<double> CycleProgress => cycleProgress;

    private readonly ReactiveProperty<double> outputMultiplier = new(1.0);
    public IReadOnlyReactiveProperty<double> OutputMultiplier => outputMultiplier;

    private readonly ReactiveProperty<double> speedMultiplier = new(1.0);
    public IReadOnlyReactiveProperty<double> SpeedMultiplier => speedMultiplier;

    private readonly ReactiveProperty<double> cycleDurationSeconds = new(0);
    public IReadOnlyReactiveProperty<double> CycleDurationSeconds => cycleDurationSeconds;

    public double AutomationCost => definition.AutomationCost;
    public double NextLevelCost => CalculateNextLevelCost();

    private readonly Subject<Unit> cycleCompleted = new();
    public IObservable<Unit> CycleCompleted => cycleCompleted;

    private readonly ReactiveProperty<int> level;
    private readonly ReactiveProperty<bool> isOwned;
    private readonly ReactiveProperty<bool> isRunning;
    private readonly ReactiveProperty<bool> isAutomated;

    public IReadOnlyReactiveProperty<int> Level => level;
    public IReadOnlyReactiveProperty<bool> IsOwned => isOwned;
    public IReadOnlyReactiveProperty<bool> IsRunning => isRunning;
    public IReadOnlyReactiveProperty<bool> IsAutomated => isAutomated;

    public string DisplayName => definition.DisplayName;

    private double ComputeCycleDurationSeconds(double speed)
    {
        if (double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0)
            speed = 1.0;

        return Math.Max(0.0001, definition.BaseCycleDurationSeconds / speed);
    }

    public GeneratorService(
        GeneratorModel model,
        GeneratorDefinition definition,
        WalletService wallet,
        TickService tickService
    )
    {
        this.model = model;

        level = new ReactiveProperty<int>(model.Level);
        isOwned = new ReactiveProperty<bool>(model.IsOwned);
        isAutomated = new ReactiveProperty<bool>(model.IsAutomated);

        // Running rule: owned generators start running immediately; unowned do not run.
        isRunning = new ReactiveProperty<bool>(model.IsOwned);

        this.definition = definition;
        this.wallet = wallet;
        this.tickService = tickService;

        cycleDurationSeconds.Value = ComputeCycleDurationSeconds(speedMultiplier.Value);
        lastIntervalSeconds = cycleDurationSeconds.Value;

        tickService.OnTick.Subscribe(_ => OnTick()).AddTo(disposables);

        // Preserve elapsed percentage when speed changes mid-cycle so progress does not jump.
        speedMultiplier
            .DistinctUntilChanged()
            .Skip(1)
            .Subscribe(newSpeed =>
            {
                // Only adjust if we are actively running a cycle.
                bool shouldRun = isAutomated.Value || isRunning.Value;
                if (!isOwned.Value || !shouldRun)
                {
                    cycleDurationSeconds.Value = ComputeCycleDurationSeconds(speedMultiplier.Value);
                    lastIntervalSeconds = cycleDurationSeconds.Value;
                    return;
                }

                double oldInterval = Math.Max(0.0001, lastIntervalSeconds);
                double percent = Math.Clamp(model.CycleElapsedSeconds / oldInterval, 0.0, 1.0);

                cycleDurationSeconds.Value = ComputeCycleDurationSeconds(speedMultiplier.Value);
                double newInterval = cycleDurationSeconds.Value;

                model.CycleElapsedSeconds = percent * newInterval;
                lastIntervalSeconds = newInterval;

                cycleProgress.Value = model.CycleElapsedSeconds / newInterval;
            })
            .AddTo(disposables);
    }

    private void OnTick()
    {
        // Unowned generators do not run
        if (!isOwned.Value)
        {
            model.CycleElapsedSeconds = 0;
            cycleProgress.Value = 0;
            isRunning.Value = false;
            return;
        }

        // Automated generators run continuously; otherwise only run when explicitly running
        bool shouldRun = isAutomated.Value || isRunning.Value;
        if (!shouldRun)
        {
            model.CycleElapsedSeconds = 0;
            cycleProgress.Value = 0;
            return;
        }

        var dt = tickService.Interval.TotalSeconds;
        // Authoritative effective duration (Option A: base / speedMultiplier)
        var interval = cycleDurationSeconds.Value;
        lastIntervalSeconds = interval;

        model.CycleElapsedSeconds += dt;

        if (isAutomated.Value)
        {
            // Handle multiple cycles if dt is larger than interval
            while (model.CycleElapsedSeconds >= interval)
            {
                model.CycleElapsedSeconds -= interval;
                Collect();
            }
        }
        else
        {
            // Manual/owned run: complete ONE cycle, then stop
            if (model.CycleElapsedSeconds >= interval)
            {
                model.CycleElapsedSeconds = 0;
                isRunning.Value = false;
            }
        }

        cycleProgress.Value = model.CycleElapsedSeconds / interval;
    }

    public void Collect()
    {
        double amount = CalculateOutput();
        wallet.IncrementBalance(CurrencyType.Cash, amount);
        cycleCompleted.OnNext(Unit.Default);
    }

    private double CalculateOutput()
    {
        // Use reactive level + multiplier as the authoritative values.
        return definition.BaseOutputPerCycle * level.Value * outputMultiplier.Value;
    }

    public void MultiplyOutput(double factor)
    {
        if (double.IsNaN(factor) || double.IsInfinity(factor))
            return;

        if (factor <= 0)
            return;

        outputMultiplier.Value *= factor;
    }

    public void MultiplySpeed(double factor)
    {
        if (double.IsNaN(factor) || double.IsInfinity(factor))
            return;

        if (factor <= 0)
            return;

        speedMultiplier.Value *= factor;

        // Keep duration in sync immediately (subscription will also run)
        cycleDurationSeconds.Value = ComputeCycleDurationSeconds(speedMultiplier.Value);
    }

    private double CalculateNextLevelCost()
    {
        // Cost to buy the *next* level (current level -> level+1)
        double baseCost = Math.Max(0, definition.BaseLevelCost);
        double growth = Math.Max(1.0, definition.LevelCostGrowth);

        int currentLevel = Math.Max(1, model.Level);
        baseCost =
            (baseCost > 0) ? baseCost
            : (model.Level == 0) ? 0
            : 1;
        return baseCost * Math.Pow(growth, currentLevel - 1);
    }

    public bool TryBuyAutomation()
    {
        if (model.IsAutomated)
            return false;

        if (wallet.CashBalance.Value < AutomationCost)
            return false;

        wallet.IncrementBalance(CurrencyType.Cash, -AutomationCost);
        model.IsAutomated = true;
        isAutomated.Value = true;

        // Automation implies continuous running from now on
        isRunning.Value = true;

        return true;
    }

    public bool TryBuyLevel()
    {
        double cost = NextLevelCost;

        if (wallet.CashBalance.Value < cost)
            return false;

        wallet.IncrementBalance(CurrencyType.Cash, -cost);
        model.Level = model.Level + 1;
        level.Value = model.Level;

        if (!model.IsOwned)
        {
            model.IsOwned = true;
            isOwned.Value = true;
            // Newly owned generators begin running immediately
            isRunning.Value = true;
        }

        return true;
    }

    public void StartRun()
    {
        if (!isOwned.Value)
            return;

        if (isAutomated.Value)
            return; // automation already runs continuously

        // Start a single cycle
        isRunning.Value = true;
    }

    public void ApplyUpgrade(UpgradeEntry upgrade)
    {
        switch (upgrade.effectType)
        {
            case UpgradeEffectType.OutputMultiplier:
                MultiplyOutput(upgrade.value);
                break;

            case UpgradeEffectType.SpeedMultiplier:
                MultiplySpeed(upgrade.value);

                // Debug: log the new effective cycle duration (Option A: duration = base / speedMultiplier)
                double newSeconds = Math.Max(
                    0.0001,
                    definition.BaseCycleDurationSeconds / speedMultiplier.Value
                );
                Debug.Log(
                    $"[GeneratorService] {definition.Id} speed x{upgrade.value:0.##} → {newSeconds:0.###}s/cycle (mult={speedMultiplier.Value:0.###})"
                );
                break;
        }
    }

    // Backwards-compat overload to accept legacy UpgradeDefinition assets.
    public void ApplyUpgrade(UpgradeDefinition upgrade)
    {
        if (upgrade == null)
            return;

        var ue = new UpgradeEntry
        {
            id = upgrade.Id,
            displayName = upgrade.DisplayName,
            generatorId = upgrade.GeneratorId,
            costSimple = upgrade.Cost,
            effectType = upgrade.EffectType,
            value = upgrade.Value,
        };

        ApplyUpgrade(ue);
    }

    public void Dispose()
    {
        cycleCompleted.OnCompleted();
        cycleCompleted.Dispose();

        level.Dispose();
        isOwned.Dispose();
        isAutomated.Dispose();
        isRunning.Dispose();
        outputMultiplier.Dispose();
        speedMultiplier.Dispose();
        cycleDurationSeconds.Dispose();

        disposables.Dispose();
    }
}
