using UniRx;
using System;
using UnityEngine;

public class GeneratorService : IDisposable
{
    private readonly GeneratorModel model;
    private readonly GeneratorDefinition definition;
    private readonly WalletService wallet;
    private readonly TickService tickService;
    
    private readonly CompositeDisposable disposables = new();
    private readonly ReactiveProperty<double> cycleProgress = new(0);
    public IReadOnlyReactiveProperty<double> CycleProgress => cycleProgress;
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
    
    public GeneratorService(GeneratorModel model, GeneratorDefinition definition, WalletService wallet, TickService tickService)
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

        tickService.OnTick
            .Subscribe(_ => OnTick())
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
        var interval = Math.Max(0.0001, definition.BaseCycleDurationSeconds);

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
        return definition.BaseOutputPerCycle * model.Level;
    }

    private double CalculateNextLevelCost()
    {
        // Cost to buy the *next* level (current level -> level+1)
        double baseCost = Math.Max(0, definition.BaseLevelCost);
        double growth = Math.Max(1.0, definition.LevelCostGrowth);

        int currentLevel = Math.Max(1, model.Level);
        baseCost = (baseCost > 0) ? baseCost : (model.Level == 0) ? 0 : 1;
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

    public void Dispose()
    {
        cycleCompleted.OnCompleted();
        cycleCompleted.Dispose();

        level.Dispose();
        isOwned.Dispose();
        isAutomated.Dispose();
        isRunning.Dispose();

        disposables.Dispose();
    }
}