using System;
using System.Globalization;
using UniRx;

public sealed class PrestigeService : IDisposable
{
    private readonly SaveService saveService;
    private readonly GameDefinitionService gameDefinitionService;
    private readonly WalletService walletService;
    private readonly GameEventStream gameEventStream;
    private readonly PrestigeDefinition prestigeDefinition;

    private readonly string prestigeResourceId;
    private readonly string lifetimeResourceId;
    private readonly string metaResourceId;

    private readonly double gainMultiplier;
    private readonly double gainOffset;
    private readonly double incomeMultiplierPerMeta;
    private readonly double incomeMultiplierBase;

    private readonly int gainMinimum = 1;

    private readonly Subject<Unit> changed = new();
    private readonly CompositeDisposable disposables = new();
    private readonly ReactiveProperty<double> lifetimeSoftEarnings = new(0d);
    private readonly ReactiveProperty<long> previewGain = new(0);
    private readonly ReactiveProperty<bool> canPrestige = new(false);
    private readonly ReactiveProperty<float> prestigeProgressRatio = new(0f);

    public bool IsEnabled { get; }
    public IReadOnlyReactiveProperty<double> CurrentMetaBalance { get; }
    public IReadOnlyReactiveProperty<double> LifetimeSoftEarnings => lifetimeSoftEarnings;
    public IReadOnlyReactiveProperty<long> PreviewGain => previewGain;
    public IReadOnlyReactiveProperty<bool> CanPrestige => canPrestige;
    public IReadOnlyReactiveProperty<float> PrestigeProgressRatio => prestigeProgressRatio;
    public IObservable<Unit> Changed => changed;

    public PrestigeService(
        GameDefinitionService gameDefinitionService,
        SaveService saveService,
        WalletService walletService,
        GameEventStream gameEventStream
    )
    {
        this.gameDefinitionService =
            gameDefinitionService ?? throw new ArgumentNullException(nameof(gameDefinitionService));
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
        this.walletService =
            walletService ?? throw new ArgumentNullException(nameof(walletService));
        this.gameEventStream =
            gameEventStream ?? throw new ArgumentNullException(nameof(gameEventStream));

        prestigeDefinition = this.gameDefinitionService.Definition?.prestige;
        if (prestigeDefinition == null || !prestigeDefinition.enabled)
        {
            IsEnabled = false;
            CurrentMetaBalance = new ReactiveProperty<double>(0d).AddTo(disposables);
            return;
        }

        IsEnabled = true;
        prestigeResourceId = NormalizeRequiredId(
            prestigeDefinition.prestigeResource,
            "prestige.prestigeResource"
        );
        lifetimeResourceId = ParseRequiredResourceFromFormulaPath(
            prestigeDefinition.formula?.basedOn,
            "prestige.formula.basedOn"
        );
        var metaUpgrade = ResolveMetaUpgrade();
        metaResourceId = ParseRequiredResourceFromFormulaPath(
            metaUpgrade.computed?.basedOn,
            "prestige.metaUpgrades[0].computed.basedOn"
        );

        if (!this.gameDefinitionService.ResourceCatalog.TryGet(prestigeResourceId, out _))
        {
            throw new InvalidOperationException(
                $"PrestigeService: prestige resource '{prestigeResourceId}' does not exist in resources."
            );
        }

        if (!this.gameDefinitionService.ResourceCatalog.TryGet(lifetimeResourceId, out _))
        {
            throw new InvalidOperationException(
                $"PrestigeService: lifetime earnings resource '{lifetimeResourceId}' does not exist in resources."
            );
        }

        if (!this.gameDefinitionService.ResourceCatalog.TryGet(metaResourceId, out _))
        {
            throw new InvalidOperationException(
                $"PrestigeService: meta resource '{metaResourceId}' does not exist in resources."
            );
        }

        gainMultiplier = prestigeDefinition.formula?.multiplier ?? 0d;
        gainOffset = prestigeDefinition.formula?.offset ?? 0d;

        var metaComputed = metaUpgrade.computed;
        incomeMultiplierPerMeta = ParseRequiredDouble(
            metaComputed?.multiplier,
            "prestige.metaUpgrades[0].computed.multiplier"
        );
        incomeMultiplierBase = ParseRequiredDouble(
            metaComputed?.offset,
            "prestige.metaUpgrades[0].computed.offset"
        );

        CurrentMetaBalance = this.walletService.GetBalanceProperty(metaResourceId);

        this.walletService.GetBalanceProperty(lifetimeResourceId)
            .DistinctUntilChanged()
            .Subscribe(_ => RefreshPreview())
            .AddTo(disposables);

        CurrentMetaBalance
            .DistinctUntilChanged()
            .Subscribe(_ => changed.OnNext(Unit.Default))
            .AddTo(disposables);

        RefreshPreview();
    }

    private double CalculateGainRaw()
    {
        if (!IsEnabled)
            return 0d;

        var lifetime = Math.Max(0d, saveService.GetLifetimeEarnings(lifetimeResourceId));
        var raw = (Math.Sqrt(lifetime) * gainMultiplier) + gainOffset;
        if (double.IsNaN(raw) || double.IsInfinity(raw))
            return 0d;

        return Math.Max(0d, raw);
    }

    public long CalculateGain()
    {
        {
            var raw = CalculateGainRaw();
            return Math.Max(0L, (long)Math.Floor(raw));
        }
    }

    public double GetIncomeMultiplierForResource(string resourceId)
    {
        if (!IsEnabled)
            return 1d;

        var id = (resourceId ?? string.Empty).Trim();
        if (!string.Equals(id, lifetimeResourceId, StringComparison.Ordinal))
            return 1d;

        var metaBalance = Math.Max(0d, CurrentMetaBalance.Value);
        var multiplier = incomeMultiplierBase + (metaBalance * incomeMultiplierPerMeta);
        if (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier <= 0d)
            return 1d;

        return multiplier;
    }

    public void PerformPrestige()
    {
        if (!IsEnabled)
            return;

        var gain = CalculateGain();
        if (gain < this.gainMinimum)
            return;

        walletService.AddRaw(prestigeResourceId, gain);

        saveService.ApplyPrestigeResetScopes(
            gameDefinitionService.Definition,
            prestigeDefinition,
            gameDefinitionService.ResourceCatalog,
            requestSave: true
        );

        gameEventStream.RequestResetSave();
    }

    public void Dispose()
    {
        changed.OnCompleted();
        changed.Dispose();
        lifetimeSoftEarnings.Dispose();
        previewGain.Dispose();
        canPrestige.Dispose();
        prestigeProgressRatio.Dispose();
        disposables.Dispose();
    }

    private void RefreshPreview()
    {
        if (!IsEnabled)
        {
            lifetimeSoftEarnings.Value = 0d;
            previewGain.Value = 0;
            canPrestige.Value = false;
            prestigeProgressRatio.Value = 0f;
            return;
        }

        var lifetime = Math.Max(0d, saveService.GetLifetimeEarnings(lifetimeResourceId));
        lifetimeSoftEarnings.Value = lifetime;

        var rawGain = CalculateGainRaw();
        var gain = Math.Max(0L, (long)Math.Floor(rawGain));

        previewGain.Value = gain;
        canPrestige.Value = gain >= this.gainMinimum;

        // Progress should always represent eligibility progress (canPrestige threshold).
        // Once eligible, keep it pinned at 1.
        prestigeProgressRatio.Value = CalculatePrestigeProgressRatio(rawGain);
        changed.OnNext(Unit.Default);
    }

    private float CalculatePrestigeProgressRatio(double rawGain)
    {
        if (gainMinimum <= 0)
            return 0f;

        var clampedRaw = Math.Max(0d, rawGain);
        var normalized = (float)(clampedRaw / gainMinimum);
        if (float.IsNaN(normalized) || float.IsInfinity(normalized))
            return 0f;

        if (normalized <= 0f)
            return 0f;
        if (normalized >= 1f)
            return 1f;
        return normalized;
    }

    private MetaUpgradeDefinition ResolveMetaUpgrade()
    {
        var upgrades = prestigeDefinition?.metaUpgrades;
        if (upgrades == null || upgrades.Length == 0 || upgrades[0] == null)
        {
            throw new InvalidOperationException(
                "PrestigeService: prestige.metaUpgrades[0] is required for the vertical slice."
            );
        }

        return upgrades[0];
    }

    private static string NormalizeRequiredId(string value, string fieldPath)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized))
            throw new InvalidOperationException($"PrestigeService: {fieldPath} is empty.");

        return normalized;
    }

    private static string ParseRequiredResourceFromFormulaPath(string path, string fieldPath)
    {
        var normalizedPath = (path ?? string.Empty).Trim();
        if (
            !ParameterizedPathParser.TryParseFormulaParameterizedPath(
                normalizedPath,
                out var parsed
            ) || string.IsNullOrEmpty(parsed.ParameterId)
        )
        {
            throw new InvalidOperationException(
                $"PrestigeService: {fieldPath} must be a parameterized resource path."
            );
        }

        return parsed.ParameterId;
    }

    private static double ParseRequiredDouble(string raw, string fieldPath)
    {
        var normalized = (raw ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized))
            throw new InvalidOperationException($"PrestigeService: {fieldPath} is empty.");

        if (
            !double.TryParse(
                normalized,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var value
            )
        )
        {
            throw new InvalidOperationException(
                $"PrestigeService: {fieldPath} value '{raw}' is not a valid number."
            );
        }

        return value;
    }
}
