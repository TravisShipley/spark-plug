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
    private readonly GameEventStream gameEventStream;

    private double lastIntervalSeconds;

    private readonly CompositeDisposable disposables = new();
    private readonly ReactiveProperty<double> cycleProgress = new(0);
    public IReadOnlyReactiveProperty<double> CycleProgress => cycleProgress;

    private readonly ReactiveProperty<double> outputMultiplier = new(1.0);
    public IReadOnlyReactiveProperty<double> OutputMultiplier => outputMultiplier;
    private readonly ReactiveProperty<double> resourceGainMultiplier = new(1.0);
    public IReadOnlyReactiveProperty<double> ResourceGainMultiplier => resourceGainMultiplier;

    private readonly ReactiveProperty<double> speedMultiplier = new(1.0);
    public IReadOnlyReactiveProperty<double> SpeedMultiplier => speedMultiplier;

    private readonly ReactiveProperty<double> cycleDurationSeconds = new(0);
    public IReadOnlyReactiveProperty<double> CycleDurationSeconds => cycleDurationSeconds;
    private readonly ReactiveProperty<double> nextLevelCost = new(0);
    private readonly ReactiveProperty<bool> canBuyLevel = new(false);
    private readonly ReactiveProperty<bool> canCollect = new(false);

    public double AutomationCost => definition.AutomationCost;
    public double BuildCost => NextLevelCost;
    public double LevelCost => NextLevelCost;
    public double NextLevelCost => nextLevelCost.Value;
    public IReadOnlyReactiveProperty<double> NextLevelCostReactive => nextLevelCost;
    public bool CanCollect => canCollect.Value;
    public IReadOnlyReactiveProperty<bool> CanCollectReactive => canCollect;
    public bool CanStart => isOwned.Value && !isAutomated.Value && !isRunning.Value;
    public bool CanBuyLevel => canBuyLevel.Value;
    public IReadOnlyReactiveProperty<bool> CanBuyLevelReactive => canBuyLevel;

    private readonly Subject<Unit> cycleCompleted = new();
    public IObservable<Unit> CycleCompleted => cycleCompleted;

    private readonly ReactiveProperty<int> level;
    private readonly ReactiveProperty<bool> isOwned;
    private readonly ReactiveProperty<bool> isRunning;
    private readonly ReactiveProperty<bool> isAutomated;
    private readonly ReactiveProperty<bool> isAutomationPurchased;
    private readonly ReactiveProperty<int> milestoneRank;
    private readonly ReactiveProperty<int> previousMilestoneAtLevel;
    private readonly ReactiveProperty<int> nextMilestoneAtLevel;
    private readonly ReactiveProperty<float> milestoneProgressRatio;
    private bool modifierAutomationEnabled;

    public IReadOnlyReactiveProperty<int> Level => level;
    public IReadOnlyReactiveProperty<bool> IsOwned => isOwned;
    public IReadOnlyReactiveProperty<bool> IsRunning => isRunning;
    public IReadOnlyReactiveProperty<bool> IsAutomated => isAutomated;
    public IReadOnlyReactiveProperty<bool> IsAutomationPurchased => isAutomationPurchased;
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
        ModifierService modifierService,
        GameEventStream gameEventStream
    )
    {
        this.model = model;

        level = new ReactiveProperty<int>(model.Level);
        isOwned = new ReactiveProperty<bool>(model.IsOwned);
        isAutomated = new ReactiveProperty<bool>(model.IsAutomated);
        isAutomationPurchased = new ReactiveProperty<bool>(model.IsAutomated);
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
        this.gameEventStream =
            gameEventStream ?? throw new ArgumentNullException(nameof(gameEventStream));

        ValidateMilestoneLevels(definition.MilestoneLevels);
        RefreshFromModifiers();
        RefreshEconomyState();
        RefreshCanCollectState();

        cycleDurationSeconds.Value = ComputeCycleDurationSeconds(speedMultiplier.Value);
        lastIntervalSeconds = cycleDurationSeconds.Value;

        tickService.OnTick.Subscribe(_ => OnTick()).AddTo(disposables);
        this.modifierService.Changed.Subscribe(_ => RefreshFromModifiers()).AddTo(disposables);

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

        level.DistinctUntilChanged().Subscribe(_ => RefreshEconomyState()).AddTo(disposables);
        isOwned.DistinctUntilChanged().Subscribe(_ => RefreshCanCollectState()).AddTo(disposables);
        isAutomated
            .DistinctUntilChanged()
            .Subscribe(_ => RefreshCanCollectState())
            .AddTo(disposables);
        isRunning
            .DistinctUntilChanged()
            .Subscribe(_ => RefreshCanCollectState())
            .AddTo(disposables);
        wallet
            .GetBalanceProperty(definition.LevelCostResourceId)
            .DistinctUntilChanged()
            .Subscribe(_ => RefreshEconomyState())
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
                QueueCompletedCyclePayout();
                Collect();
            }
        }
        else
        {
            // Manual/owned run: complete ONE cycle, then stop
            if (model.CycleElapsedSeconds >= interval)
            {
                model.CycleElapsedSeconds = 0;
                QueueCompletedCyclePayout();
                isRunning.Value = false;
            }
        }

        cycleProgress.Value = model.CycleElapsedSeconds / interval;
    }

    public void Collect()
    {
        if (!model.HasPendingPayout || model.PendingPayout <= 0)
            return;

        var amount = model.PendingPayout;
        model.PendingPayout = 0;
        model.HasPendingPayout = false;

        wallet.AddRaw(definition.OutputResourceId, amount);
        RefreshCanCollectState();
        cycleCompleted.OnNext(Unit.Default);
    }

    public void HandleCollectPressed(double cashGenerated)
    {
        gameEventStream.PublishIncrementBalance(definition.OutputResourceId, cashGenerated);
    }

    private double CalculateOutput()
    {
        // Use reactive level + multiplier as the authoritative values.
        return definition.BaseOutputPerCycle * level.Value * outputMultiplier.Value;
    }

    private void QueueCompletedCyclePayout()
    {
        var payout = CalculateOutput();
        var gainMultiplier = resourceGainMultiplier.Value;
        if (double.IsNaN(gainMultiplier) || double.IsInfinity(gainMultiplier) || gainMultiplier <= 0)
            gainMultiplier = 1.0;

        model.PendingPayout += payout * gainMultiplier;
        model.HasPendingPayout = model.PendingPayout > 0;
        RefreshCanCollectState();
    }

    private double CalculateNextLevelCost()
    {
        // Cost to buy the *next* level (current level -> level+1)
        double baseCost = Math.Max(0, definition.BaseLevelCost);
        double growth = Math.Max(1.0, definition.LevelCostGrowth);

        // Use the reactive level as the authoritative value.
        int currentLevel = Math.Max(1, level.Value);

        baseCost =
            (baseCost > 0) ? baseCost
            : (level.Value == 0) ? 0
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
        isAutomationPurchased.Value = true;
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

    public int TryBuyLevels(int maxToBuy)
    {
        int purchaseLimit = Math.Max(0, maxToBuy);
        if (purchaseLimit <= 0)
            return 0;

        int purchased = 0;
        for (int i = 0; i < purchaseLimit; i++)
        {
            if (!TryBuyLevel())
                break;

            purchased++;
        }

        return purchased;
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
        isAutomationPurchased.Dispose();
        milestoneRank.Dispose();
        previousMilestoneAtLevel.Dispose();
        nextMilestoneAtLevel.Dispose();
        milestoneProgressRatio.Dispose();
        isRunning.Dispose();
        outputMultiplier.Dispose();
        resourceGainMultiplier.Dispose();
        speedMultiplier.Dispose();
        cycleDurationSeconds.Dispose();
        nextLevelCost.Dispose();
        canBuyLevel.Dispose();
        canCollect.Dispose();

        disposables.Dispose();
    }

    private static CostItem[] CreateCost(string resourceId, double amount)
    {
        return new[]
        {
            new CostItem
            {
                resource = resourceId,
                amount = amount.ToString(CultureInfo.InvariantCulture),
            },
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

        var newResourceGainMult = modifierService.GetResourceGainMultiplier(definition.OutputResourceId);
        if (
            double.IsNaN(newResourceGainMult)
            || double.IsInfinity(newResourceGainMult)
            || newResourceGainMult <= 0
        )
        {
            newResourceGainMult = 1.0;
        }
        resourceGainMultiplier.Value = newResourceGainMult;

        var previousSpeedMult = speedMultiplier.Value;
        var newSpeedMult = modifierService.GetNodeSpeedMultiplier(definition.Id);
        if (double.IsNaN(newSpeedMult) || double.IsInfinity(newSpeedMult) || newSpeedMult <= 0)
            newSpeedMult = 1.0;
        speedMultiplier.Value = newSpeedMult;

        if (!previousAutomation && modifierAutomationEnabled && isOwned.Value)
            isRunning.Value = true;

        RefreshEconomyState();
        RefreshCanCollectState();
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

    private void RefreshEconomyState()
    {
        nextLevelCost.Value = CalculateNextLevelCost();
        canBuyLevel.Value =
            wallet.GetBalance(definition.LevelCostResourceId) >= nextLevelCost.Value;
    }

    private void RefreshCanCollectState()
    {
        canCollect.Value =
            isOwned.Value
            && !isAutomated.Value
            && !isRunning.Value
            && model.HasPendingPayout
            && model.PendingPayout > 0;
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
