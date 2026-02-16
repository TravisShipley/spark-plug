using System;
using System.Globalization;
using UniRx;
using UnityEngine;

public class GeneratorService : IDisposable
{
    private readonly GeneratorModel model;
    private readonly GeneratorDefinition definition;
    private readonly WalletService wallet;
    private readonly TickService tickService;
    private readonly ModifierService modifierService;

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
    private readonly ReactiveProperty<int> milestoneRank;
    private readonly ReactiveProperty<int> previousMilestoneAtLevel;
    private readonly ReactiveProperty<int> nextMilestoneAtLevel;
    private readonly ReactiveProperty<float> milestoneProgressRatio;
    private bool modifierAutomationEnabled;

    public IReadOnlyReactiveProperty<int> Level => level;
    public IReadOnlyReactiveProperty<bool> IsOwned => isOwned;
    public IReadOnlyReactiveProperty<bool> IsRunning => isRunning;
    public IReadOnlyReactiveProperty<bool> IsAutomated => isAutomated;
    public IReadOnlyReactiveProperty<int> MilestoneRank => milestoneRank;
    public IReadOnlyReactiveProperty<int> PreviousMilestoneAtLevel => previousMilestoneAtLevel;
    public IReadOnlyReactiveProperty<int> NextMilestoneAtLevel => nextMilestoneAtLevel;
    public IReadOnlyReactiveProperty<float> MilestoneProgressRatio => milestoneProgressRatio;
    public string Id => model.Id;

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
        TickService tickService,
        ModifierService modifierService
    )
    {
        this.model = model;

        level = new ReactiveProperty<int>(model.Level);
        isOwned = new ReactiveProperty<bool>(model.IsOwned);
        isAutomated = new ReactiveProperty<bool>(model.IsAutomated);
        milestoneRank = new ReactiveProperty<int>(0);
        previousMilestoneAtLevel = new ReactiveProperty<int>(0);
        nextMilestoneAtLevel = new ReactiveProperty<int>(0);
        milestoneProgressRatio = new ReactiveProperty<float>(0f);

        // Running rule: owned generators start running immediately; unowned do not run.
        isRunning = new ReactiveProperty<bool>(model.IsOwned);

        this.definition = definition;
        this.wallet = wallet;
        this.tickService = tickService;
        this.modifierService =
            modifierService ?? throw new ArgumentNullException(nameof(modifierService));

        ValidateMilestoneLevels(definition.MilestoneLevels);
        RefreshFromModifiers();

        cycleDurationSeconds.Value = ComputeCycleDurationSeconds(speedMultiplier.Value);
        lastIntervalSeconds = cycleDurationSeconds.Value;

        tickService.OnTick.Subscribe(_ => OnTick()).AddTo(disposables);
        this.modifierService
            .Changed.Subscribe(_ => RefreshFromModifiers())
            .AddTo(disposables);

        // Preserve elapsed percentage when speed changes mid-cycle so progress does not jump.
        speedMultiplier
            .DistinctUntilChanged()
            .Subscribe(newSpeed =>
            {
                // Only adjust if we are actively running a cycle.
                bool shouldRun = IsAutomationActive() || isRunning.Value;
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

        level.DistinctUntilChanged().Subscribe(RefreshMilestoneProgress).AddTo(disposables);
        RefreshMilestoneProgress(level.Value);
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
        bool shouldRun = IsAutomationActive() || isRunning.Value;
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
        wallet.Add(definition.OutputResourceId, amount);
        cycleCompleted.OnNext(Unit.Default);
    }

    private double CalculateOutput()
    {
        // Use reactive level + multiplier as the authoritative values.
        return definition.BaseOutputPerCycle * level.Value * outputMultiplier.Value;
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

        if (!wallet.TrySpend(CreateCost(definition.AutomationCostResourceId, AutomationCost)))
            return false;
        model.IsAutomated = true;
        isAutomated.Value = IsAutomationActive();

        // Automation implies continuous running from now on
        isRunning.Value = true;

        return true;
    }

    public bool TryBuyLevel()
    {
        double cost = NextLevelCost;

        if (!wallet.TrySpend(CreateCost(definition.LevelCostResourceId, cost)))
            return false;
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
        milestoneRank.Dispose();
        previousMilestoneAtLevel.Dispose();
        nextMilestoneAtLevel.Dispose();
        milestoneProgressRatio.Dispose();
        isRunning.Dispose();
        outputMultiplier.Dispose();
        speedMultiplier.Dispose();
        cycleDurationSeconds.Dispose();

        disposables.Dispose();
    }

    private static CostItem[] CreateCost(string resourceId, double amount)
    {
        return new[]
        {
            new CostItem
            {
                resource = resourceId,
                amount = amount.ToString(CultureInfo.InvariantCulture)
            }
        };
    }

    private bool IsAutomationActive()
    {
        return model.IsAutomated || modifierAutomationEnabled;
    }

    private void RefreshFromModifiers()
    {
        var previousAutomation = modifierAutomationEnabled;
        modifierAutomationEnabled = modifierService.IsNodeAutomationEnabled(definition.Id);
        isAutomated.Value = IsAutomationActive();

        var newOutputMult = modifierService.GetNodeOutputMultiplier(
            definition.Id,
            definition.OutputResourceId
        );
        if (double.IsNaN(newOutputMult) || double.IsInfinity(newOutputMult) || newOutputMult <= 0)
            newOutputMult = 1.0;
        outputMultiplier.Value = newOutputMult;

        var previousSpeedMult = speedMultiplier.Value;
        var newSpeedMult = modifierService.GetNodeSpeedMultiplier(definition.Id);
        if (double.IsNaN(newSpeedMult) || double.IsInfinity(newSpeedMult) || newSpeedMult <= 0)
            newSpeedMult = 1.0;
        speedMultiplier.Value = newSpeedMult;
        if (Math.Abs(previousSpeedMult - newSpeedMult) <= 0.0000001)
        {
            cycleDurationSeconds.Value = ComputeCycleDurationSeconds(newSpeedMult);
            lastIntervalSeconds = cycleDurationSeconds.Value;
        }

        if (!previousAutomation && modifierAutomationEnabled && isOwned.Value)
            isRunning.Value = true;
    }

    private void RefreshMilestoneProgress(int currentLevel)
    {
        var levels = definition.MilestoneLevels;
        if (levels == null || levels.Length == 0)
        {
            milestoneRank.Value = 0;
            previousMilestoneAtLevel.Value = 0;
            nextMilestoneAtLevel.Value = 0;
            milestoneProgressRatio.Value = 1f;
            return;
        }

        int rank = 0;
        int previous = 0;
        int next = 0;
        for (int i = 0; i < levels.Length; i++)
        {
            var atLevel = levels[i];
            if (atLevel <= currentLevel)
            {
                rank++;
                previous = atLevel;
            }
            else
            {
                next = atLevel;
                break;
            }
        }

        milestoneRank.Value = rank;
        previousMilestoneAtLevel.Value = previous;
        nextMilestoneAtLevel.Value = next;

        if (next > 0)
        {
            var span = next - previous;
            if (span > 0)
            {
                milestoneProgressRatio.Value = Mathf.Clamp01(
                    (float)(currentLevel - previous) / span
                );
            }
            else
            {
                milestoneProgressRatio.Value = 1f;
            }
        }
        else
        {
            milestoneProgressRatio.Value = 1f;
        }
    }

    private static void ValidateMilestoneLevels(int[] levels)
    {
        if (levels == null || levels.Length <= 1)
            return;

        for (int i = 1; i < levels.Length; i++)
        {
            if (levels[i] < levels[i - 1])
            {
                Debug.LogWarning(
                    "GeneratorService: MilestoneLevels should be sorted ascending for deterministic milestone rank."
                );
                return;
            }
        }
    }
}
