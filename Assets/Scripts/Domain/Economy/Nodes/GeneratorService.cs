using System;
using System.Globalization;
using UniRx;
using UnityEngine;

public class GeneratorService : IDisposable
{
    private const int MaxAffordableProbeLevels = 5000;
    private readonly GeneratorModel model;
    private readonly GeneratorDefinition definition;
    private readonly WalletService wallet;
    private readonly TickService tickService;
    private readonly ModifierService modifierService;
    private readonly ComputedVarService computedVarService;
    private readonly SaveService saveService;
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
    private bool isInitializing;

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
        ComputedVarService computedVarService,
        SaveService saveService,
        GameEventStream gameEventStream
    )
    {
        this.model = model;
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        this.tickService = tickService ?? throw new ArgumentNullException(nameof(tickService));
        this.modifierService =
            modifierService ?? throw new ArgumentNullException(nameof(modifierService));
        this.computedVarService = computedVarService;
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
        this.gameEventStream =
            gameEventStream ?? throw new ArgumentNullException(nameof(gameEventStream));

        level = new ReactiveProperty<int>(model.Level);
        isOwned = new ReactiveProperty<bool>(model.IsOwned);
        isAutomated = new ReactiveProperty<bool>(model.IsAutomated);
        isAutomationPurchased = new ReactiveProperty<bool>(model.IsAutomated);
        milestoneRank = new ReactiveProperty<int>(0);
        previousMilestoneAtLevel = new ReactiveProperty<int>(0);
        nextMilestoneAtLevel = new ReactiveProperty<int>(0);
        milestoneProgressRatio = new ReactiveProperty<float>(0f);
        isRunning = new ReactiveProperty<bool>(false);
        isInitializing = true;

        ValidateMilestoneLevels(definition.MilestoneLevels);
        RefreshFromModifiers();
        cycleDurationSeconds.Value = ComputeCycleDurationSeconds(speedMultiplier.Value);
        lastIntervalSeconds = cycleDurationSeconds.Value;
        NormalizeLoadedRuntimeState();
        isRunning.Value = ResolveInitialRunningState();
        RefreshEconomyState();
        RefreshCanCollectState();
        isInitializing = false;
        PersistRuntimeState(requestSave: false);

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
                PersistRuntimeState(requestSave: false);
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
            model.PendingPayout = 0;
            model.HasPendingPayout = false;
            cycleProgress.Value = 0;
            isRunning.Value = false;
            PersistRuntimeState(requestSave: false);
            return;
        }

        // Automated generators run continuously; otherwise only run when explicitly running
        bool shouldRun = IsAutomationActive() || isRunning.Value;
        if (!shouldRun)
        {
            if (!model.HasPendingPayout)
            {
                model.CycleElapsedSeconds = 0;
                cycleProgress.Value = 0;
            }

            PersistRuntimeState(requestSave: false);
            return;
        }

        var dt = tickService.Interval.TotalSeconds;
        var zoneStateChanged = ApplyRateStateDeltaOutputs(dt);
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
                zoneStateChanged |= ApplyCycleStateDeltaOutputs();
                QueueCompletedCyclePayout();
                CollectInternal(requestSave: false, publishCycleCompleted: true);
            }
        }
        else
        {
            // Manual/owned run: complete ONE cycle, then stop
            if (model.CycleElapsedSeconds >= interval)
            {
                model.CycleElapsedSeconds = 0;
                zoneStateChanged |= ApplyCycleStateDeltaOutputs();
                QueueCompletedCyclePayout();
                isRunning.Value = false;
                PersistRuntimeState(requestSave: true);
            }
        }

        zoneStateChanged |= saveService.ClampZoneStateVars(definition.ZoneId, requestSave: true);
        cycleProgress.Value = model.CycleElapsedSeconds / interval;
        if (isAutomated.Value || model.WasRunning || model.CycleElapsedSeconds > 0d)
            PersistRuntimeState(requestSave: false);
    }

    public void Collect()
    {
        CollectInternal(requestSave: true, publishCycleCompleted: true);
    }

    private void CollectInternal(bool requestSave, bool publishCycleCompleted)
    {
        if (!model.HasPendingPayout || model.PendingPayout <= 0)
            return;

        var amount = model.PendingPayout;
        model.PendingPayout = 0;
        model.HasPendingPayout = false;

        wallet.AddRaw(definition.OutputResourceId, amount);
        RefreshCanCollectState();
        PersistRuntimeState(requestSave);

        if (publishCycleCompleted)
            cycleCompleted.OnNext(Unit.Default);
    }

    private double CalculateOutput()
    {
        return CalculatePrimaryResourceOutputPerCycle() * level.Value * outputMultiplier.Value;
    }

    private void QueueCompletedCyclePayout()
    {
        model.PendingPayout += CalculateCompletedCyclePayoutAmount();
        model.HasPendingPayout = model.PendingPayout > 0;
        RefreshCanCollectState();
    }

    private double CalculateNextLevelCost()
    {
        return CalculateLevelCostForCurrentLevel(level.Value);
    }

    private double CalculateLevelCostForCurrentLevel(int currentLevel)
    {
        // Cost to buy the *next* level (current level -> level+1)
        double baseCost = Math.Max(0, definition.BaseLevelCost);
        double growth = Math.Max(1.0, definition.LevelCostGrowth);
        int rawCurrentLevel = currentLevel;

        // Cost equation uses at least level 1 for the exponent base.
        currentLevel = Math.Max(1, currentLevel);

        baseCost =
            (baseCost > 0) ? baseCost
            : (rawCurrentLevel == 0) ? 0
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
        PersistRuntimeState(requestSave: true);

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
            PersistRuntimeState(requestSave: true);
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

    public int TryBuyByMode(BuyModeDefinition mode)
    {
        return TryBuyByMode(mode, int.MaxValue);
    }

    public int TryBuyByMode(BuyModeDefinition mode, int maxToBuy)
    {
        var plannedCount = CalculatePlannedPurchaseCount(mode, maxToBuy);
        if (plannedCount <= 0)
            return 0;

        return TryBuyLevels(plannedCount);
    }

    public void StartRun()
    {
        if (!isOwned.Value)
            return;

        if (isAutomated.Value)
            return; // automation already runs continuously

        // Start a single cycle
        isRunning.Value = true;
        model.CycleElapsedSeconds = Math.Max(0d, model.CycleElapsedSeconds);
        PersistRuntimeState(requestSave: true);
    }

    private int ResolveLevelsToNextMilestone()
    {
        var milestoneLevels = definition.MilestoneLevels ?? Array.Empty<int>();
        if (milestoneLevels.Length == 0)
            return 1;

        var current = level.Value;
        for (int i = 0; i < milestoneLevels.Length; i++)
        {
            var milestoneLevel = milestoneLevels[i];
            if (milestoneLevel <= current)
                continue;

            return Math.Max(1, milestoneLevel - current);
        }

        return 1;
    }

    public int CalculatePlannedPurchaseCount(BuyModeDefinition mode, int maxToBuy = int.MaxValue)
    {
        if (mode == null)
            throw new InvalidOperationException("GeneratorService: BuyMode is null.");

        var cappedMaxToBuy = Math.Max(0, maxToBuy);
        if (cappedMaxToBuy <= 0)
            return 0;

        var requestedCount = ResolveRequestedCountForMode(mode, cappedMaxToBuy);
        if (requestedCount <= 0)
            return 0;

        var affordableCount = CalculateAffordableLevels(requestedCount);
        if (IsAllOrNothingMode(mode))
            return affordableCount >= requestedCount ? requestedCount : 0;

        return affordableCount;
    }

    public double CalculatePlannedPurchaseCost(BuyModeDefinition mode, int maxToBuy = int.MaxValue)
    {
        if (mode == null)
            throw new InvalidOperationException("GeneratorService: BuyMode is null.");

        var cappedMaxToBuy = Math.Max(0, maxToBuy);
        if (cappedMaxToBuy <= 0)
            return 0d;

        var requestedCount = ResolveRequestedCountForMode(mode, cappedMaxToBuy);
        if (requestedCount <= 0)
            return 0d;

        if (IsAllOrNothingMode(mode))
            return CalculateTotalCostForLevels(requestedCount);

        var plannedCount = CalculatePlannedPurchaseCount(mode, cappedMaxToBuy);
        if (plannedCount <= 0)
            return 0d;

        return CalculateTotalCostForLevels(plannedCount);
    }

    public double CalculateDisplayPurchaseCost(BuyModeDefinition mode, int maxToBuy = int.MaxValue)
    {
        if (mode == null)
            throw new InvalidOperationException("GeneratorService: BuyMode is null.");

        var cappedMaxToBuy = Math.Max(0, maxToBuy);
        if (cappedMaxToBuy <= 0)
            return 0d;

        var requestedCount = ResolveRequestedCountForMode(mode, cappedMaxToBuy);
        if (requestedCount <= 0)
            return 0d;

        if (!IsMaxAffordableMode(mode))
            return CalculateTotalCostForLevels(requestedCount);

        var plannedCost = CalculatePlannedPurchaseCost(mode, cappedMaxToBuy);
        if (plannedCost > 0d)
            return plannedCost;

        // MAX still shows the single-level entry price even when nothing is currently affordable.
        return CalculateSingleLevelCost();
    }

    private int ResolveRequestedCountForMode(BuyModeDefinition mode, int cappedMaxToBuy)
    {
        var kind = (mode.kind ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(kind))
            throw new InvalidOperationException(
                $"GeneratorService: BuyMode '{mode.id}' has empty kind."
            );

        if (string.Equals(kind, "fixed", StringComparison.OrdinalIgnoreCase))
        {
            var fixedCount = mode.fixedCount;
            if (fixedCount < 1)
            {
                throw new InvalidOperationException(
                    $"GeneratorService: BuyMode '{mode.id}' fixedCount must be >= 1."
                );
            }

            return Math.Min(fixedCount, cappedMaxToBuy);
        }

        if (string.Equals(kind, "nextMilestone", StringComparison.OrdinalIgnoreCase))
        {
            var levelsToNextMilestone = ResolveLevelsToNextMilestone();
            return Math.Min(Math.Max(1, levelsToNextMilestone), cappedMaxToBuy);
        }

        if (string.Equals(kind, "maxAffordable", StringComparison.OrdinalIgnoreCase))
        {
            return cappedMaxToBuy;
        }

        throw new InvalidOperationException(
            $"GeneratorService: Unsupported BuyMode kind '{kind}' for mode '{mode.id}'."
        );
    }

    private static bool IsAllOrNothingMode(BuyModeDefinition mode)
    {
        var kind = (mode?.kind ?? string.Empty).Trim();
        return string.Equals(kind, "fixed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "nextMilestone", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMaxAffordableMode(BuyModeDefinition mode)
    {
        var kind = (mode?.kind ?? string.Empty).Trim();
        return string.Equals(kind, "maxAffordable", StringComparison.OrdinalIgnoreCase);
    }

    private double CalculateSingleLevelCost()
    {
        return CalculateTotalCostForLevels(1);
    }

    private int CalculateAffordableLevels(int requestedCount)
    {
        if (requestedCount <= 0)
            return 0;

        var remaining = wallet.GetBalance(definition.LevelCostResourceId);
        if (remaining <= 0d)
            return 0;

        // TODO: Replace this probe loop with a closed-form/binary-search approach for large levels.
        var probeLimit = Math.Min(requestedCount, MaxAffordableProbeLevels);
        int affordable = 0;
        int currentLevel = level.Value;
        for (int i = 0; i < probeLimit; i++)
        {
            var levelForCost = currentLevel + i;
            var cost = CalculateLevelCostForCurrentLevel(levelForCost);
            if (double.IsNaN(cost) || double.IsInfinity(cost) || cost <= 0d)
                break;

            if (remaining + double.Epsilon < cost)
                break;

            remaining -= cost;
            affordable++;
        }

        return affordable;
    }

    private double CalculateTotalCostForLevels(int count)
    {
        if (count <= 0)
            return 0d;

        double total = 0d;
        int currentLevel = level.Value;
        for (int i = 0; i < count; i++)
        {
            var levelForCost = currentLevel + i;
            var cost = CalculateLevelCostForCurrentLevel(levelForCost);
            if (double.IsNaN(cost) || double.IsInfinity(cost) || cost <= 0d)
                break;

            total += cost;
        }

        return total;
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

        var newResourceGainMult = modifierService.GetResourceGainMultiplier(
            definition.OutputResourceId
        );
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
        if (!isInitializing)
            PersistRuntimeState(requestSave: false);
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

    private void NormalizeLoadedRuntimeState()
    {
        model.CycleElapsedSeconds = SanitizeNonNegative(model.CycleElapsedSeconds);
        model.PendingPayout = SanitizeNonNegative(model.PendingPayout);

        if (!isOwned.Value)
        {
            if (model.WasRunning || model.HasPendingPayout || model.CycleElapsedSeconds > 0d || model.PendingPayout > 0d)
            {
                Debug.LogError(
                    $"GeneratorService[{definition.Id}]: Loaded runtime snapshot for an unowned generator. Clearing invalid runtime state."
                );
            }

            model.WasRunning = false;
            model.HasPendingPayout = false;
            model.CycleElapsedSeconds = 0d;
            model.PendingPayout = 0d;
            cycleProgress.Value = 0d;
            return;
        }

        if (model.HasPendingPayout && model.PendingPayout <= 0d)
        {
            Debug.LogError(
                $"GeneratorService[{definition.Id}]: Loaded pending-payout state without a payout amount. Reconstructing one cycle of payout."
            );
            model.PendingPayout = CalculateCompletedCyclePayoutAmount();
            model.HasPendingPayout = model.PendingPayout > 0d;
        }

        if (model.HasPendingPayout)
        {
            if (model.WasRunning || model.CycleElapsedSeconds > 0d)
            {
                Debug.LogError(
                    $"GeneratorService[{definition.Id}]: Loaded both running progress and pending payout. Preserving pending payout and clearing progress."
                );
            }

            model.WasRunning = false;
            model.CycleElapsedSeconds = 0d;
            cycleProgress.Value = 0d;
            return;
        }

        var interval = Math.Max(0.0001d, cycleDurationSeconds.Value);

        if (IsAutomationActive() && !model.WasRunning)
        {
            Debug.LogError(
                $"GeneratorService[{definition.Id}]: Automated generator loaded idle without pending payout. Resuming continuous automation."
            );
            model.WasRunning = true;
        }

        if (!model.WasRunning && model.CycleElapsedSeconds > 0d)
        {
            Debug.LogError(
                $"GeneratorService[{definition.Id}]: Loaded elapsed time for a non-running generator. Clearing elapsed time."
            );
            model.CycleElapsedSeconds = 0d;
        }

        if (model.CycleElapsedSeconds < interval)
        {
            cycleProgress.Value = interval > 0d ? model.CycleElapsedSeconds / interval : 0d;
            return;
        }

        if (IsAutomationActive())
        {
            model.CycleElapsedSeconds %= interval;
            model.WasRunning = true;
            cycleProgress.Value = interval > 0d ? model.CycleElapsedSeconds / interval : 0d;
            return;
        }

        Debug.LogError(
            $"GeneratorService[{definition.Id}]: Loaded completed manual cycle without pending payout. Converting it to a waiting-to-collect state."
        );
        model.CycleElapsedSeconds = 0d;
        model.PendingPayout = CalculateCompletedCyclePayoutAmount();
        model.HasPendingPayout = model.PendingPayout > 0d;
        model.WasRunning = false;
        cycleProgress.Value = 0d;
    }

    private bool ResolveInitialRunningState()
    {
        if (!isOwned.Value)
            return false;

        if (model.HasPendingPayout)
            return false;

        if (IsAutomationActive())
            return true;

        return model.WasRunning;
    }

    private void PersistRuntimeState(bool requestSave)
    {
        var running =
            isOwned.Value
            && !model.HasPendingPayout
            && (IsAutomationActive() || isRunning.Value);

        model.WasRunning = running;

        saveService.SetNodeInstanceRuntimeState(
            definition.Id,
            model.WasRunning,
            model.HasPendingPayout,
            model.CycleElapsedSeconds,
            model.PendingPayout,
            requestSave
        );
    }

    private static double SanitizeNonNegative(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0d;

        return Math.Max(0d, value);
    }

    private double CalculateCompletedCyclePayoutAmount()
    {
        var payout = CalculateOutput();
        var gainMultiplier = resourceGainMultiplier.Value;
        if (
            double.IsNaN(gainMultiplier)
            || double.IsInfinity(gainMultiplier)
            || gainMultiplier <= 0
        )
        {
            gainMultiplier = 1.0;
        }

        return payout * gainMultiplier;
    }

    private double CalculatePrimaryResourceOutputPerCycle()
    {
        var output = ResolvePrimaryResourceOutput();
        if (output == null)
            return 0d;

        return ResolveOutputAmountPerCycle(output);
    }

    private bool ApplyRateStateDeltaOutputs(double dt)
    {
        if (definition.Outputs == null || definition.Outputs.Count == 0 || dt <= 0d)
            return false;

        var changed = false;
        for (int i = 0; i < definition.Outputs.Count; i++)
        {
            var output = definition.Outputs[i];
            if (!IsStateDeltaOutput(output) || !IsRateOutput(output))
                continue;

            var varId = NormalizeId(output.varId);
            if (string.IsNullOrEmpty(varId))
                continue;

            var deltaPerSecond = ResolveRateAmountPerSecond(output);
            if (deltaPerSecond == 0d)
                continue;

            changed |= saveService.AddZoneStateVar(
                definition.ZoneId,
                varId,
                deltaPerSecond * dt,
                requestSave: true
            );
        }

        return changed;
    }

    private bool ApplyCycleStateDeltaOutputs()
    {
        if (definition.Outputs == null || definition.Outputs.Count == 0)
            return false;

        var changed = false;
        for (int i = 0; i < definition.Outputs.Count; i++)
        {
            var output = definition.Outputs[i];
            if (!IsStateDeltaOutput(output) || !IsCycleOutput(output))
                continue;

            var varId = NormalizeId(output.varId);
            if (string.IsNullOrEmpty(varId))
                continue;

            var delta = ResolveOutputAmountPerCycle(output);
            if (delta == 0d)
                continue;

            changed |= saveService.AddZoneStateVar(
                definition.ZoneId,
                varId,
                delta,
                requestSave: true
            );
        }

        return changed;
    }

    private NodeOutputDefinition ResolvePrimaryResourceOutput()
    {
        if (definition.Outputs == null || definition.Outputs.Count == 0)
            return null;

        for (int i = 0; i < definition.Outputs.Count; i++)
        {
            var output = definition.Outputs[i];
            if (output == null || IsStateDeltaOutput(output))
                continue;

            if (
                string.Equals(
                    NormalizeId(output.resource),
                    NormalizeId(definition.OutputResourceId),
                    StringComparison.Ordinal
                )
            )
            {
                return output;
            }
        }

        for (int i = 0; i < definition.Outputs.Count; i++)
        {
            var output = definition.Outputs[i];
            if (output != null && !IsStateDeltaOutput(output))
                return output;
        }

        return null;
    }

    private double ResolveOutputAmountPerCycle(NodeOutputDefinition output)
    {
        if (output == null)
            return 0d;

        var cycleDuration = Math.Max(0.0001d, cycleDurationSeconds.Value);
        if (IsRateOutput(output))
        {
            var perSecond = ResolveRateAmountPerSecond(output);
            return perSecond * cycleDuration;
        }

        var fromVar = ResolveFormulaAmount(output.amountPerCycleFromVar);
        if (fromVar.HasValue)
            return fromVar.Value;

        var fromState = ResolveFormulaAmount(output.amountPerCycleFromState);
        if (fromState.HasValue)
            return fromState.Value;

        if (output.basePayout != 0d)
            return output.basePayout;

        if (output.amountPerCycle != 0d)
            return output.amountPerCycle;

        if (output.basePerSecond != 0d)
            return output.basePerSecond * cycleDuration;

        return 0d;
    }

    private double ResolveRateAmountPerSecond(NodeOutputDefinition output)
    {
        if (output == null)
            return 0d;

        if (output.basePerSecond != 0d)
            return output.basePerSecond;

        var fromVar = ResolveFormulaAmount(output.amountPerCycleFromVar);
        if (fromVar.HasValue)
            return fromVar.Value;

        var fromState = ResolveFormulaAmount(output.amountPerCycleFromState);
        if (fromState.HasValue)
            return fromState.Value;

        return 0d;
    }

    private double? ResolveFormulaAmount(string raw)
    {
        var value = NormalizeId(raw);
        if (string.IsNullOrEmpty(value))
            return null;

        if (
            double.TryParse(
                value,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var literal
            )
        )
        {
            return literal;
        }

        if (ParameterizedPathParser.TryParseFormulaParameterizedPath(value, out _))
            return computedVarService?.ResolvePathOrZero(value, definition.ZoneId) ?? 0d;

        return computedVarService?.EvaluateOrZero(value, definition.ZoneId) ?? 0d;
    }

    private static bool IsStateDeltaOutput(NodeOutputDefinition output)
    {
        return string.Equals(
            NormalizeId(output?.kind),
            "stateDelta",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsRateOutput(NodeOutputDefinition output)
    {
        return string.Equals(NormalizeId(output?.mode), "rate", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCycleOutput(NodeOutputDefinition output)
    {
        var mode = NormalizeId(output?.mode);
        return string.IsNullOrEmpty(mode)
            || string.Equals(mode, "cycle", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeId(string value)
    {
        return (value ?? string.Empty).Trim();
    }
}
